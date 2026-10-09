using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationCaution)]
internal class ModeratorActionEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom) {
            return Task.CompletedTask;
        }

        var currentRoom = session.GetHabbo().CurrentRoom;

        if (currentRoom == null) {
            return Task.CompletedTask;
        }

        var alertMode = packet.ReadInt();
        var alertMessage = packet.ReadString();
        var isCaution = alertMode != 3;
        alertMessage = isCaution ? $"Caution from Moderator:\n\n{alertMessage}" : $"Message from Moderator:\n\n{alertMessage}";
        currentRoom.SendPacket(new BroadcastMessageAlertComposer(alertMessage));

        return Task.CompletedTask;
    }
}
