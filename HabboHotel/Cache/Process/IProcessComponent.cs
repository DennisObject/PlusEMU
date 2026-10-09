namespace Plus.HabboHotel.Cache.Process;

public interface IProcessComponent : IDisposable
{
    /// <summary>
    /// Initializes the ProcessComponent.
    /// </summary>
    void Init(Action sweep);
}
