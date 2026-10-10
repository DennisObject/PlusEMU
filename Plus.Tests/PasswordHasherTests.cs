using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities.DependencyInjection;
using Xunit;

namespace Plus.Tests;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new Argon2idPasswordHasher();

    [Fact]
    public void HashEmitsOwaspMinimumArgon2idPhcString()
    {
        var hash = _hasher.Hash("correct horse battery");

        var parts = hash.Split('$');
        Assert.Equal(6, parts.Length);
        Assert.Equal("", parts[0]);
        Assert.Equal("argon2id", parts[1]);
        Assert.Equal("v=19", parts[2]);
        Assert.Equal("m=19456,t=2,p=1", parts[3]);
        Assert.Equal(16, Convert.FromBase64String(Pad(parts[4])).Length);
        Assert.Equal(32, Convert.FromBase64String(Pad(parts[5])).Length);
        Assert.DoesNotContain("=", parts[4] + parts[5]);
    }

    [Fact]
    public void HashUsesAFreshSaltEveryTime()
    {
        Assert.NotEqual(_hasher.Hash("same password"), _hasher.Hash("same password"));
    }

    [Fact]
    public void VerifyAcceptsTheRightPasswordAndRejectsAWrongOne()
    {
        var hash = _hasher.Hash("correct horse battery");

        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("correct horse battery", hash));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("correct horse batterY", hash));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("", hash));
    }

    [Fact]
    public void VerifyAsksForARehashWhenStoredParametersAreWeaker()
    {
        var weak = PhcHash("old password", memoryKiB: 8192, iterations: 1);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, _hasher.Verify("old password", weak));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("wrong password", weak));
    }

    [Fact]
    public void VerifyAcceptsStrongerStoredParametersWithoutRehash()
    {
        var strong = PhcHash("strong password", memoryKiB: 32768, iterations: 3);

        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("strong password", strong));
    }

    [Fact]
    public void LegacyPlaintextRowMatchesOnceAndAsksForUpgrade()
    {
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, _hasher.Verify("VoltLocal-2026", "VoltLocal-2026"));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("voltlocal-2026", "VoltLocal-2026"));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("VoltLocal-2026x", "VoltLocal-2026"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingStoredPasswordNeverMatches(string? stored)
    {
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("", stored!));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("anything", stored!));
    }

    [Theory]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2FsdA$")]
    [InlineData("$argon2id$v=16$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=abc,t=2,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=99999999,t=2,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$%%%$aGFzaGhhc2hoYXNoaGFzaA")]
    public void MalformedArgon2idValuesFailInsteadOfBeingTreatedAsPlaintext(string stored)
    {
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify(stored, stored));
    }

    [Fact]
    public void EmulatorContainerResolvesTheArgon2idHasherAsASingleton()
    {
        // Program registers every [Singleton] interface through AddAssignableTo.
        Assert.NotNull(typeof(IPasswordHasher).GetCustomAttributes(typeof(SingletonAttribute), false).SingleOrDefault());
        var provider = new ServiceCollection().AddAssignableTo(typeof(Program).Assembly, typeof(IPasswordHasher)).BuildServiceProvider();

        var hasher = Assert.IsType<Argon2idPasswordHasher>(provider.GetRequiredService<IPasswordHasher>());
        Assert.Same(hasher, provider.GetRequiredService<IPasswordHasher>());
    }

    private static string PhcHash(string password, int memoryKiB, int iterations)
    {
        var salt = Encoding.ASCII.GetBytes("0123456789abcdef");
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password)) { Salt = salt, MemorySize = memoryKiB, Iterations = iterations, DegreeOfParallelism = 1 };
        var hash = argon.GetBytes(32);

        return $"$argon2id$v=19$m={memoryKiB},t={iterations},p=1${Convert.ToBase64String(salt).TrimEnd('=')}${Convert.ToBase64String(hash).TrimEnd('=')}";
    }

    private static string Pad(string value) => value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
}
