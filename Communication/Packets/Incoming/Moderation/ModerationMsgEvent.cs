using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationAlert)]
internal class ModerationMsgEvent : IPacketEvent
{
    private readonly IGameClientManager _clientManager;

    public ModerationMsgEvent(IGameClientManager clientManager)
    {
        _clientManager = clientManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var message = packet.ReadString();
        var client = _clientManager.GetClientByUserId(userId);

        if (client == null)
        {
            return Task.CompletedTask;
        }

        if (client.GetHabbo() == null || !session.GetHabbo().Access.Outranks(client.GetHabbo().Access))
        {
            return Task.CompletedTask;
        }

        client.SendNotification(message);

        return Task.CompletedTask;
    }
}
