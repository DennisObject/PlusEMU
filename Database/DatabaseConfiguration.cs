namespace Plus.Database;

public class DatabaseConfiguration
{
    public string Hostname { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public uint Port
    {
        get; set;
    }
    public uint MinimumPoolSize
    {
        get; set;
    }
    public uint MaximumPoolSize
    {
        get; set;
    }
}
