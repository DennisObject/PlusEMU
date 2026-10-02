namespace Plus.HabboHotel.Items.Wired.Settings;

[Flags]
public enum WiredRoomAccess
{
    None = 0, Everyone = 1, Rights = 2, GroupMembers = 4, GroupAdmins = 8
}

public sealed record WiredRoomSettingsSnapshot(int InspectMask = 2, int ModifyMask = 2, string TimeZoneId = "")
{
    public static bool TryValidate(int inspect, int modify, string? timezone, out WiredRoomSettingsSnapshot validated)
    {
        validated = new();
        if ((inspect & ~15) != 0 || (modify & ~14) != 0 || timezone == null || timezone.Length > 64)
            return false;
        try
        {
            if (timezone.Length != 0) _ = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
        // Current Octane/Polaris settings keep every modifier able to inspect; admins are group members.
        if ((modify & 4) != 0) modify |= 8;
        inspect |= modify;
        if ((inspect & 4) != 0) inspect |= 8;
        validated = new(inspect, modify, timezone);
        return true;
    }
}

public interface IWiredRoomSettingsStore
{
    WiredRoomSettingsSnapshot? Load(uint roomId);
    void Save(uint roomId, int actorId, bool staff, WiredRoomSettingsSnapshot? expected, WiredRoomSettingsSnapshot settings);
}

public sealed record WiredRoomSettingsView(WiredRoomSettingsSnapshot Settings, bool CanInspect, bool CanModify, bool CanManage);
