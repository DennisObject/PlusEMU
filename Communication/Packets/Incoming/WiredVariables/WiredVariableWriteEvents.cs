using System.Text;
using Plus.Communication.Packets.Incoming.Rooms;
using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.WiredVariables;

public sealed record WiredVariableMenuWrite(int Action, WiredVariableTarget Target, int TargetId, uint DefinitionId, int Value, string Token);

public sealed class WiredUserVariableUpdateEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, false, true) || !TryRead(packet, false, out var request)) return Task.CompletedTask;
        var menu = new WiredVariableMenu(room, room.GetWired().Variables);
        menu.Write(request!.Target, request.TargetId, request.DefinitionId, request.Value, WiredVariableMutation.Set, request.Token);
        session.Send(new WiredUserVariablesDataComposer(menu.Snapshot()));
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

public sealed class WiredUserVariableManageEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, false, true) || !WiredUserVariableUpdateEvent.TryRead(packet, true, out var request)) return Task.CompletedTask;
        var menu = new WiredVariableMenu(room, room.GetWired().Variables);
        if (request!.Action == 2)
        {
            // This reaches offline holders, so ordinary wired editing or group rights are insufficient.
            if (room.CheckRights(session, true, false)) menu.Clear(request.Target, request.DefinitionId);
        }
        else menu.Write(request.Target, request.TargetId, request.DefinitionId, request.Value,
            request.Action == 1 ? WiredVariableMutation.Remove : WiredVariableMutation.Replace);
        session.Send(new WiredUserVariablesDataComposer(menu.Snapshot()));
        return Task.CompletedTask;
    }
}
