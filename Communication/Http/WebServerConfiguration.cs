namespace Plus.Communication.Http;

/// <summary>
/// "WebServer" section of config.json. Every value has a working default, so the server
/// still listens on 127.0.0.1:8080 when the section is missing.
/// </summary>
public class WebServerConfiguration
{
    public string Hostname { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
}
