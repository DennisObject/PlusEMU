namespace Plus.HabboHotel.Rooms.Chat.Styles;

public interface IChatStyleManager
{
    void Init();
    IReadOnlyList<int> GetAllowedStyleIds(Plus.HabboHotel.Permissions.UserAccess access);
    bool TryGetStyle(int id, out ChatStyle style);
}