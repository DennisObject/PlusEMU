namespace Plus.HabboHotel.Rooms.PathFinding;

public interface IMovementEngine
{
    void Tick();
}

internal sealed class LegacyMovementEngine(Action cycle) : IMovementEngine
{
    public void Tick() => cycle();
}
