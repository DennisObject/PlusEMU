using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.AI;

public abstract class BotAi
{
    private Room? _room;
    private uint _roomId;
    private RoomUser? _roomUser;
    private int _roomUserId;
    public int BaseId;

    public void Init(int baseId, int roomUserId, uint roomId, RoomUser user, Room room)
    {
        BaseId = baseId;
        _roomUserId = roomUserId;
        _roomId = roomId;
        _roomUser = user;
        _room = room;
    }

    public Room? GetRoom() => _room;

    public RoomUser? GetRoomUser() => _roomUser;

    public RoomBot? GetBotData() => _roomUser?.BotData;

    public void Detach(Room room, RoomUser user)
    {
        if (!ReferenceEquals(_room, room) || !ReferenceEquals(_roomUser, user)) return;
        _room = null;
        _roomUser = null;
    }

    public abstract void OnSelfEnterRoom();
    public abstract void OnSelfLeaveRoom(bool kicked);
    public abstract void OnUserEnterRoom(RoomUser user);
    public abstract void OnUserLeaveRoom(GameClient client);
    public abstract void OnUserSay(RoomUser user, string message);
    public abstract void OnUserShout(RoomUser user, string message);
    public abstract void OnTimerTick();
}
