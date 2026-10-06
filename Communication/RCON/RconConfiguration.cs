namespace Plus.Communication.RCON;

public class RconConfiguration
{
    public string Hostname
    {
        get; set;
    }
    public int Port
    {
        get; set;
    }
    public IEnumerable<string> AllowedAddresses
    {
        get; set;
    }
}
