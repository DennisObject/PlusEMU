using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Extensions.Logging;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Registration;

namespace Plus.Communication.Http;

public interface IAuthHttpServer
{
    Task Start();
    Task Stop();

    /// <summary>Bound addresses once started, e.g. http://127.0.0.1:8080.</summary>
    IReadOnlyCollection<string> Urls { get; }
}

/// <summary>
/// Kestrel host for the hotel's login API (/api/auth/*, /api/health, /api/maintenance).
/// TLS is terminated by the reverse proxy in front of it.
/// </summary>
public class AuthHttpServer : IAuthHttpServer
{
    public const string RateLimitPolicy = "auth";
    public const long MaxRequestBodyBytes = 16 * 1024;

    private readonly AuthApiConfiguration _configuration;
    private readonly AuthEndpoints _endpoints;
    private WebApplication? _app;

    public AuthHttpServer(IOptions<AuthApiConfiguration> options, ILoginService login, IRegistrationService registration, ISsoTicketStore ssoTickets,
        IAccessTokenStore accessTokens)
    {
        _configuration = options.Value;
        _endpoints = new(login, registration, ssoTickets, accessTokens);
    }

    public IReadOnlyCollection<string> Urls => _app?.Urls.ToList() ?? [];

    public async Task Start()
    {
        // Production pins the generic error handler; Development would add the stack-trace page.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], EnvironmentName = Environments.Production });
        builder.Logging.ClearProviders().AddNLog().AddFilter("Microsoft", LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(ConfigureKestrel);
        builder.Services.Configure<ForwardedHeadersOptions>(ConfigureForwardedHeaders);
        builder.Services.AddRateLimiter(ConfigureRateLimiter);

        var app = builder.Build();
        // With no known proxies at all the middleware would trust every sender, so it only runs
        // when proxies are configured.
        if (_configuration.TrustedProxies.Length > 0)
            app.UseForwardedHeaders();
        app.UseExceptionHandler(error => error.Run(context => WriteError(context, StatusCodes.Status500InternalServerError, "Something went wrong. Please try again.")));
        app.UseStatusCodePages(context => WriteError(context.HttpContext, context.HttpContext.Response.StatusCode, ErrorFor(context.HttpContext.Response.StatusCode)));
        app.Use(AddSecurityHeaders);
        app.UseRateLimiter();
        _endpoints.Map(app);

        await app.StartAsync();
        _app = app;
    }

    public async Task Stop()
    {
        if (_app == null)
            return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    /// <summary>The caller's address after X-Forwarded-For from a trusted proxy is applied.</summary>
    public static string ClientAddress(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address == null)
            return "unknown";
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    private void ConfigureKestrel(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions kestrel)
    {
        kestrel.AddServerHeader = false;
        kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        kestrel.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
        kestrel.Limits.MaxRequestLineSize = 4 * 1024;
        kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);

        if (string.Equals(_configuration.Hostname, "localhost", StringComparison.OrdinalIgnoreCase))
            kestrel.ListenLocalhost(_configuration.Port);
        else
            kestrel.Listen(IPAddress.Parse(_configuration.Hostname), _configuration.Port);
    }

    private void ConfigureForwardedHeaders(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
        options.ForwardLimit = 1;
        // Only the configured proxies are trusted, not the framework's loopback default.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in _configuration.TrustedProxies)
        {
            if (proxy.Contains('/'))
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(proxy));
            else
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        }
    }

    private void ConfigureRateLimiter(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, _) => new(WriteError(context.HttpContext, StatusCodes.Status429TooManyRequests, AuthEndpoints.TooManyAttempts));
        options.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(ClientAddress(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = _configuration.RequestsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    }

    private static Task AddSecurityHeaders(HttpContext context, Func<Task> next)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return next();
    }

    private static string ErrorFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => "Not found.",
        StatusCodes.Status405MethodNotAllowed => "Method not allowed.",
        StatusCodes.Status413PayloadTooLarge => "Request too large.",
        StatusCodes.Status429TooManyRequests => AuthEndpoints.TooManyAttempts,
        >= 500 => "Something went wrong. Please try again.",
        _ => "Invalid request."
    };

    private static Task WriteError(HttpContext context, int status, string error)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(new { error });
    }
}
