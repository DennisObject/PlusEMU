namespace Plus.Core;

public interface IServerStatusUpdater
{
    void Dispose();
    void Init();
    void OnTick(object obj);
}
