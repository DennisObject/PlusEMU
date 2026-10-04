using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Core.Language;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationKick)]
internal class ModerationKickEvent : IPacketEvent
{
    private readonly IGameClientManager _clientManager;
    private readonly ILanguageManager _languageManager;

    public ModerationKickEvent(IGameClientManager clientManager, ILanguageManager languageManager)
    {
        _clientManager = clientManager;
        _languageManager = languageManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        packet.ReadString(); //message
        var client = _clientManager.GetClientByUserId(userId);
        if (client == null || client.GetHabbo() == null || client.GetHabbo().CurrentRoom == null || client.GetHabbo().Id == session.GetHabbo().Id)
            return Task.CompletedTask;
        if (!session.GetHabbo().Access.Outranks(client.GetHabbo().Access))
        {
            session.SendNotification(_languageManager.TryGetValue("moderation.kick.disallowed"));
            return Task.CompletedTask;
        }
        session.GetHabbo().CurrentRoom?.GetRoomUserManager().RemoveUserFromRoom(client, true);
        return Task.CompletedTask;
    }
}