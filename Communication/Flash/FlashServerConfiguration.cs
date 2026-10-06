using Plus.Communication.Abstractions;

namespace Plus.Communication.Flash;

public class FlashServerConfiguration : IGameServerOptions
{
    public string Name => "Flash";
    public string Hostname { get; set; } = "127.0.0.1";
    public int Port { get; set; }
}
