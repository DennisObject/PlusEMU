namespace Plus.Communication.RCON;

public class RconConfiguration
{
    public string Hostname { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public IEnumerable<string> AllowedAddresses { get; set; } = [];
}
