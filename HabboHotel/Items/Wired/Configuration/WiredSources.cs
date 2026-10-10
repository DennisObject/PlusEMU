namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Volt/Polaris protocol values. The named slot determines whether a source targets furni, users or bots.</summary>
public static class WiredSources
{
    public const int Trigger = 0;
    public const int ReachedUser = 10;
    public const int ClickedUser = 11;
    public const int Selected = 100;
    public const int BotByName = 100;
    public const int Snapshot = 101;
    public const int UserByName = 101;
    public const int Selector = 200;
    public const int Signal = 201;
    public const int AllRoom = 900;
}
