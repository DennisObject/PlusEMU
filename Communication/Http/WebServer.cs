using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Extensions.Logging;
using Plus.HabboHotel.Badges.Rarity;
using Plus.HabboHotel.Catalog;

namespace Plus.Communication.Http;

public interface IWebServer
{
    Task Start();
    Task Stop();

    /// <summary>Bound addresses once started, e.g. http://127.0.0.1:8080.</summary>
    IReadOnlyCollection<string> Urls { get; }
}

/// <summary>Machine-readable "code" sent next to "error" in every error response.</summary>
public static class WebErrors
{
    public const string InvalidRequest = "invalid_request";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string PayloadTooLarge = "payload_too_large";
    public const string ServerError = "server_error";
}

/// <summary>
/// Kestrel host for the badge leaderboard (/api/badges/leaderboard) and furnidata
/// (/api/gamedata/furnidata). TLS is terminated by the reverse proxy in front of it.
/// </summary>
public class WebServer : IWebServer
{
    public const long MaxRequestBodyBytes = 16 * 1024;

    private readonly WebServerConfiguration _configuration;
    private readonly BadgeLeaderboardEndpoints _badgeLeaderboard;
    private readonly FurnidataEndpoints _furnidata;
    private WebApplication? _app;

    public WebServer(IOptions<WebServerConfiguration> options, IBadgeRarityManager badgeRarity, ICatalogFurnidata furnidata)
    {
        _configuration = options.Value;
        _badgeLeaderboard = new(badgeRarity);
        _furnidata = new(furnidata);
    }

    public IReadOnlyCollection<string> Urls => _app?.Urls.ToList() ?? [];

    internal IServiceProvider? Services => _app?.Services;

    public async Task Start()
    {
        // The empty builder reads no appsettings or ASPNETCORE_* variables and watches no files:
        // config.json is the only source. Production keeps stack traces out of error responses.
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = [], EnvironmentName = Environments.Production });
        builder.Logging.AddNLog(new NLogProviderOptions { RemoveLoggerFactoryFilter = false }).AddFilter("Microsoft", LogLevel.Warning);
        builder.WebHost.UseKestrelCore().ConfigureKestrel(ConfigureKestrel);
        builder.Services.AddRoutingCore();
        builder.Services.AddSingleton<IHostLifetime, EmulatorOwnedLifetime>();

        var app = builder.Build();

        app.UseExceptionHandler(error => error.Run(context => WriteError(context, StatusCodes.Status500InternalServerError)));
        app.UseStatusCodePages(context => WriteError(context.HttpContext, context.HttpContext.Response.StatusCode));
        app.Use(AddSecurityHeaders);
        _badgeLeaderboard.Map(app);
        _furnidata.Map(app);

        await app.StartAsync();
        _app = app;
    }

    public async Task Stop()
    {
        if (_app == null) {
            return;
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    private void ConfigureKestrel(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions kestrel)
    {
        kestrel.AddServerHeader = false;
        kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        kestrel.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
        kestrel.Limits.MaxRequestLineSize = 4 * 1024;
        kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        // Trickled bodies are dropped after 5 seconds below 240 bytes/s.
        kestrel.Limits.MinRequestBodyDataRate = new(240, TimeSpan.FromSeconds(5));
        kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);

        if (string.Equals(_configuration.Hostname, "localhost", StringComparison.OrdinalIgnoreCase)) {
            kestrel.ListenLocalhost(_configuration.Port);
        }
        else {
            kestrel.Listen(IPAddress.Parse(_configuration.Hostname), _configuration.Port);
        }
    }

    private static Task AddSecurityHeaders(HttpContext context, Func<Task> next)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";

        return next();
    }

    private static (string Code, string Error) ErrorFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => (WebErrors.NotFound, "Not found."),
        StatusCodes.Status405MethodNotAllowed => (WebErrors.MethodNotAllowed, "Method not allowed."),
        StatusCodes.Status413PayloadTooLarge => (WebErrors.PayloadTooLarge, "Request too large."),
        >= 500 => (WebErrors.ServerError, "Something went wrong. Please try again."),
        _ => (WebErrors.InvalidRequest, "Invalid request.")
    };

    private static Task WriteError(HttpContext context, int status)
    {
        var (code, error) = ErrorFor(status);
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";

        return context.Response.WriteAsJsonAsync(new { error, code });
    }

    /// <summary>
    /// The emulator owns process shutdown (Program handles SIGTERM/SIGINT and calls Stop). The
    /// default ConsoleLifetime would cancel the signal and wait for a host Run() that never comes.
    /// </summary>
    private sealed class EmulatorOwnedLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
