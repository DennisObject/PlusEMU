namespace Plus.Communication.Http;

/// <summary>
/// "AuthApi" section of config.json. Every value has a working default, so the login API
/// still starts on 127.0.0.1:8080 when the section is missing.
/// </summary>
public class AuthApiConfiguration
{
    public string Hostname { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;

    /// <summary>Proxy IPs or CIDR ranges whose X-Forwarded-For header is trusted. Empty trusts nobody.</summary>
    public string[] TrustedProxies { get; set; } = [];

    public int SsoTicketLifetimeSeconds { get; set; } = 300;
    public int AccessTokenLifetimeMinutes { get; set; } = 1440;

    /// <summary>Requests per minute per client IP across /api/auth.</summary>
    public int RequestsPerMinute { get; set; } = 60;

    public int FailedLoginWindowMinutes { get; set; } = 15;
    public int MaxFailedLoginsPerAccount { get; set; } = 10;
    public int MaxFailedLoginsPerAddress { get; set; } = 30;

    public RegistrationDefaults Registration { get; set; } = new();
}

/// <summary>Columns a newly registered user starts with.</summary>
public class RegistrationDefaults
{
    public string Look { get; set; } = "hd-180-1.hr-100-61.ch-210-66.lg-270-82.sh-290-80";
    public string Motto { get; set; } = "Octane";
    public int Credits { get; set; } = 50000;
    public int ActivityPoints { get; set; } = 5000;
    public int Rank { get; set; } = 1;
    public bool Vip { get; set; } = true;
    public int HomeRoom { get; set; } = 1;

    /// <summary>Extra name fragments refused at registration, on top of the in-game staff fragments.</summary>
    public string[] ReservedNames { get; set; } = [];
}
