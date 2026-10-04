using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Settings;

internal class GetRoomRightsEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return Task.CompletedTask;
        var instance = session.GetHabbo().CurrentRoom;
        if (instance == null)
            return Task.CompletedTask;
        if (!instance.CheckRights(session))
            return Task.CompletedTask;
        session.Send(new RoomRightsListComposer(instance.Id, instance.UsersWithRights
            .Select(id => PlusEnvironment.Game.CacheManager.GenerateUser(id))
            .Select(user => user == null
                ? new RoomRightHolder(0, "Unknown Error")
                : new RoomRightHolder(user.Id, user.Username))
            .ToArray()));
        return Task.CompletedTask;
    }
}
