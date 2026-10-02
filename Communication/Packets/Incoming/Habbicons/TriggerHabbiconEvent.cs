using Plus.Communication.Packets.Outgoing.Habbicons;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;
using Plus.HabboHotel.Quests;
using Plus.Utilities;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class TriggerHabbiconEvent(IHabbiconService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;
        var user = room?.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        int id = packet.ReadInt();
        if (room == null || user == null || id <= 0 || id > 1000000) return Task.CompletedTask;
        if (Environment.TickCount64 - habbo.LastHabbiconTrigger < 1000) return Task.CompletedTask;
        if (UnixTimestamp.GetNow() < habbo.FloodTime || habbo.TimeMuted > 0 ||
            (!habbo.Permissions.HasRight("room_ignore_mute") && room.CheckMute(session))) return Task.CompletedTask;
        if (!habbo.Permissions.HasRight("mod_tool") && user.IncrementAndCheckFlood(out var muteTime))
        {
            session.Send(new FloodControlComposer(muteTime));
            return Task.CompletedTask;
        }
        if (!service.Use(habbo.Id, id)) return Task.CompletedTask;
        RewardTrackManager.Current?.Progress(session, RewardTrackActions.UseHabbicon);
        habbo.LastHabbiconTrigger = Environment.TickCount64;
        user.UnIdle();
        room.SendPacket(new RoomUseHabbiconComposer(user.VirtualId, id));
        session.Send(new UserHabbiconsComposer(service.Load(habbo.Id)));
        return Task.CompletedTask;
    }
}
