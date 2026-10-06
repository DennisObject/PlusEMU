using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Xunit;

namespace Plus.Tests;

public class AuthApiConfigurationTests
{
    private static AuthApiConfiguration Bind(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        Program.AddConfiguration<AuthApiConfiguration>(services, configuration.GetSection("AuthApi"));

        return services.BuildServiceProvider().GetRequiredService<IOptions<AuthApiConfiguration>>().Value;
    }

    [Fact]
    public void ShippedConfigBindsExactlyAsWritten()
    {
        var path = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../Config/config.json"));
        var auth = Bind(new ConfigurationBuilder().AddJsonFile(path).Build());

        Assert.Equal("127.0.0.1", auth.Hostname);
        Assert.Equal(8080, auth.Port);
        Assert.Equal(["127.0.0.1", "::1"], auth.TrustedProxies);
        Assert.Equal(["staff"], auth.Registration.ReservedNames);
        Assert.Equal(50000, auth.Registration.Credits);
    }

    [Fact]
    public void MissingSectionStillStartsTheApiOnLoopbackWithoutTrustingProxies()
    {
        var auth = Bind(new ConfigurationBuilder().Build());

        Assert.Equal("127.0.0.1", auth.Hostname);
        Assert.Equal(8080, auth.Port);
        Assert.Empty(auth.TrustedProxies);
        Assert.Equal(300, auth.SsoTicketLifetimeSeconds);
        Assert.Equal("hd-180-1.hr-100-61.ch-210-66.lg-270-82.sh-290-80", auth.Registration.Look);
    }
}
