using Plus.Communication.RCON.Commands.User;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

/// <summary>Each RCON command decodes its declared primitives and delegates exactly once; malformed input never reaches the service.</summary>
public sealed class UserMaintenanceCommandTests
{
    [Theory]
    [MemberData(nameof(GiveRows))]
    public async Task GiveDecodesIdAndAmountAndDelegatesTheCurrencyText(string[] parameters, string call, bool result)
    {
        var service = new RecordingService { Result = result };

        Assert.Equal(result, await new GiveUserCurrencyCommand(service).TryExecute(parameters));
        Assert.Equal(new[] { call }, service.Calls);
    }

    public static TheoryData<string[], string, bool> GiveRows() => new()
    {
        { new[] { "7", "coins", "-5" }, "give 7 coins -5", true },
        { new[] { "7", "", "5" }, "give 7  5", false },
        { new[] { "2147483647", "gotw", "1" }, "give 2147483647 gotw 1", false },
    };

    [Theory]
    [MemberData(nameof(GiveMalformed))]
    public async Task GiveRejectsShortOrNonIntegerPrimitivesWithoutCallingTheService(string[] parameters)
    {
        var service = new RecordingService { Result = true };

        Assert.False(await new GiveUserCurrencyCommand(service).TryExecute(parameters));
        Assert.Empty(service.Calls);
    }

    public static TheoryData<string[]> GiveMalformed() => new()
    {
        { new[] { "7", "coins" } },
        { new[] { "x", "coins", "5" } },
        { new[] { "7", "coins", "x" } },
        { new[] { "7", "coins", "2147483648" } },
    };

    [Theory]
    [MemberData(nameof(TakeRows))]
    public async Task TakeDecodesIdAndSignedAmountAndDelegates(string[] parameters, string call)
    {
        var service = new RecordingService { Result = true };

        Assert.True(await new TakeUserCurrencyCommand(service).TryExecute(parameters));
        Assert.Equal(new[] { call }, service.Calls);
    }

    public static TheoryData<string[], string> TakeRows() => new()
    {
        { new[] { "7", "pixels", "5" }, "take 7 pixels 5" },
        { new[] { "7", "pixels", "-5" }, "take 7 pixels -5" },
    };

    [Theory]
    [MemberData(nameof(TakeMalformed))]
    public async Task TakeRejectsShortOrNonIntegerPrimitivesWithoutCallingTheService(string[] parameters)
    {
        var service = new RecordingService { Result = true };

        Assert.False(await new TakeUserCurrencyCommand(service).TryExecute(parameters));
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task SyncDecodesIdAndCurrencyAndDelegates()
    {
        var service = new RecordingService { Result = false };

        Assert.False(await new SyncUserCurrencyCommand(service).TryExecute(["7", "gotw"]));
        Assert.Equal(new[] { "sync 7 gotw" }, service.Calls);
    }

    public static TheoryData<string[]> TakeMalformed() => new()
    {
        { new[] { "7", "diamonds" } },
        { new[] { "x", "diamonds", "5" } },
        { new[] { "7", "diamonds", "x" } },
    };

    [Theory]
    [MemberData(nameof(SyncMalformed))]
    public async Task SyncRejectsShortOrNonIntegerPrimitivesWithoutCallingTheService(string[] parameters)
    {
        var service = new RecordingService { Result = true };

        Assert.False(await new SyncUserCurrencyCommand(service).TryExecute(parameters));
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task ReloadCurrencyDecodesIdAndCurrencyAndDelegates()
    {
        var service = new RecordingService { Result = true };

        Assert.True(await new ReloadUserCurrencyCommand(service).TryExecute(["7", "credits"]));
        Assert.Equal(new[] { "reload 7 credits" }, service.Calls);
    }

    public static TheoryData<string[]> SyncMalformed() => new()
    {
        { new[] { "7" } },
        { new[] { "x", "gotw" } },
    };

    [Theory]
    [MemberData(nameof(ReloadMalformed))]
    public async Task ReloadCurrencyRejectsShortOrNonIntegerPrimitivesWithoutCallingTheService(string[] parameters)
    {
        var service = new RecordingService { Result = true };

        Assert.False(await new ReloadUserCurrencyCommand(service).TryExecute(parameters));
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task MottoDecodesTheIdAndDelegates()
    {
        var service = new RecordingService { Result = true };

        Assert.True(await new ReloadUserMottoCommand(service).TryExecute(["7"]));
        Assert.Equal(new[] { "motto 7" }, service.Calls);
    }

    public static TheoryData<string[]> ReloadMalformed() => new()
    {
        { new[] { "7" } },
        { new[] { "x", "credits" } },
    };

    [Theory]
    [MemberData(nameof(MottoMalformed))]
    public async Task MottoRejectsMissingOrNonIntegerIdWithoutCallingTheService(string[] parameters)
    {
        var service = new RecordingService { Result = true };

        Assert.False(await new ReloadUserMottoCommand(service).TryExecute(parameters));
        Assert.Empty(service.Calls);
    }

    public static TheoryData<string[]> MottoMalformed() => new()
    {
        { Array.Empty<string>() },
        { new[] { "x" } },
    };

    private sealed class RecordingService : IUserMaintenanceService
    {
        public bool Result { get; init; }
        public List<string> Calls { get; } = [];
        public Task<bool> GiveCurrency(int userId, string currency, int amount) { Calls.Add($"give {userId} {currency} {amount}"); return Task.FromResult(Result); }
        public Task<bool> TakeCurrency(int userId, string currency, int amount) { Calls.Add($"take {userId} {currency} {amount}"); return Task.FromResult(Result); }
        public Task<bool> SyncCurrency(int userId, string currency) { Calls.Add($"sync {userId} {currency}"); return Task.FromResult(Result); }
        public Task<bool> ReloadCurrency(int userId, string currency) { Calls.Add($"reload {userId} {currency}"); return Task.FromResult(Result); }
        public Task<bool> ReloadMotto(int userId) { Calls.Add($"motto {userId}"); return Task.FromResult(Result); }
    }
}
