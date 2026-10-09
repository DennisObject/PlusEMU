using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plus.Communication.Flash;
using Plus.Communication.Nitro;
using Plus.Communication.RCON;
using Xunit;

namespace Plus.Tests;

public class GameServerConfigurationTests
{
    [Fact]
    public void MissingOptionalStringsUseServerNameAndLoopbackDefaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Nitro:Port"] = "2096",
            ["Flash:Port"] = "1232"
        }).Build();
        using var provider = Bind(configuration);
        var nitro = provider.GetRequiredService<IOptions<NitroServerConfiguration>>().Value;
        var flash = provider.GetRequiredService<IOptions<FlashServerConfiguration>>().Value;

        Assert.Equal("Nitro", nitro.Name);
        Assert.Equal("127.0.0.1", nitro.Hostname);
        Assert.Equal(2096, nitro.Port);
        Assert.Equal("Flash", flash.Name);
        Assert.Equal("127.0.0.1", flash.Hostname);
        Assert.Equal(1232, flash.Port);
    }

    [Fact]
    public void ConfiguredServerValuesOverrideDefaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Nitro:Name"] = "Custom Nitro",
            ["Nitro:Hostname"] = "192.0.2.10",
            ["Nitro:Port"] = "2200",
            ["Flash:Hostname"] = "192.0.2.11",
            ["Flash:Port"] = "1200"
        }).Build();
        using var provider = Bind(configuration);
        var nitro = provider.GetRequiredService<IOptions<NitroServerConfiguration>>().Value;
        var flash = provider.GetRequiredService<IOptions<FlashServerConfiguration>>().Value;

        Assert.Equal("Custom Nitro", nitro.Name);
        Assert.Equal("192.0.2.10", nitro.Hostname);
        Assert.Equal(2200, nitro.Port);
        Assert.Equal("192.0.2.11", flash.Hostname);
        Assert.Equal(1200, flash.Port);
    }

    [Fact]
    public void RconDefaultsToLoopbackWithNoAllowedAddresses()
    {
        var rcon = Rcon(new() { ["Rcon:Port"] = "30001" });

        Assert.Equal("127.0.0.1", rcon.Hostname);
        Assert.Equal(30001, rcon.Port);
        Assert.Empty(rcon.AllowedAddresses);
    }

    [Fact]
    public void RconBindsConfiguredHostAndAllowedAddresses()
    {
        var rcon = Rcon(new()
        {
            ["Rcon:Hostname"] = "192.0.2.12",
            ["Rcon:AllowedAddresses:0"] = "127.0.0.1",
            ["Rcon:AllowedAddresses:1"] = "localhost"
        });

        Assert.Equal("192.0.2.12", rcon.Hostname);
        Assert.Equal(["127.0.0.1", "localhost"], rcon.AllowedAddresses);
    }

    private static RconConfiguration Rcon(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        Program.AddConfiguration<RconConfiguration>(services, new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Rcon"));
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<RconConfiguration>>().Value;
    }

    private static ServiceProvider Bind(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        Program.AddConfiguration<NitroServerConfiguration>(services, configuration.GetSection("Nitro"));
        Program.AddConfiguration<FlashServerConfiguration>(services, configuration.GetSection("Flash"));

        return services.BuildServiceProvider();
    }
}
