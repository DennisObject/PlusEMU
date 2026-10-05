using System.Text;
using Plus.Communication.Packets.Incoming.Rooms;
using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.WiredVariables;

public sealed class WiredUserVariableUpdateEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.GetWired().Settings.CanModify(session) || !TryRead(packet, false, out var request)) return Task.CompletedTask;
        session.Send(new WiredUserVariablesDataComposer(menus.Write(room, request!)));
        return Task.CompletedTask;
    }
    public static bool TryRead(IIncomingPacket packet, bool manage, out WiredVariableMenuWrite? request)
    {
        request = null;
        try
        {
            var action = manage ? packet.ReadInt() : 0;
            var target = packet.ReadInt(); var targetId = packet.ReadInt(); var definitionId = packet.ReadInt(); var value = packet.ReadInt();
            var token = !manage && packet.HasDataRemaining() ? packet.ReadString() : "";
            if (action is < 0 or > 2 || target is not (0 or 1 or 3) || definitionId < 0 || packet.HasDataRemaining()
                || Encoding.UTF8.GetByteCount(token) > 64 || token.Length > 0 && (definitionId != 0 || !token.StartsWith("internal:@", StringComparison.Ordinal))
                || token.Length == 0 && definitionId == 0) return false;
            request = new(action, (WiredVariableTarget)target, targetId, (uint)definitionId, value, token); return true;
        }
        catch (ArgumentException) { return false; }
    }
}

public sealed class WiredUserVariableManageEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var settings = room.GetWired().Settings;
        if (!settings.CanModify(session) || !WiredUserVariableUpdateEvent.TryRead(packet, true, out var request)) return Task.CompletedTask;
        // This reaches offline holders, so ordinary wired editing or group rights are insufficient.
        var mayClear = request!.Action == 2 && settings.CanManage(session);
        session.Send(new WiredUserVariablesDataComposer(menus.Manage(room, request, mayClear)));
        return Task.CompletedTask;
    }
}
