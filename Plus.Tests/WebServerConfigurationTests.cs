using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Xunit;

namespace Plus.Tests;

public class WebServerConfigurationTests
{
    private static WebServerConfiguration Bind(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        Program.AddConfiguration<WebServerConfiguration>(services, configuration.GetSection("WebServer"));

        return services.BuildServiceProvider().GetRequiredService<IOptions<WebServerConfiguration>>().Value;
    }

    [Fact]
    public void ShippedConfigBindsExactlyAsWritten()
    {
        var path = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../Config/config.json"));
        var web = Bind(new ConfigurationBuilder().AddJsonFile(path).Build());

        Assert.Equal("127.0.0.1", web.Hostname);
        Assert.Equal(8080, web.Port);
    }

    [Fact]
    public void MissingSectionStillListensOnLoopback()
    {
        var web = Bind(new ConfigurationBuilder().Build());

        Assert.Equal("127.0.0.1", web.Hostname);
        Assert.Equal(8080, web.Port);
    }
}
