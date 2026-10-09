using Plus.Communication.Packets.Incoming.Rooms;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.WiredVariables;

public sealed class WiredVariableInspectionRequestEvent(IWiredVariableInspectionService inspections) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (TryRead(packet, out var request)) {
            inspections.Read(room, session, request!);
        }

        return Task.CompletedTask;
    }

    public static bool TryRead(IIncomingPacket packet, out WiredVariableInspectionRequest? request)
    {
        request = null;

        try {
            if (packet.ReadInt() != 1) {
                return false;
            }

            var requestId = packet.ReadInt();
            var roomId = packet.ReadInt();
            var target = packet.ReadInt();
            var entityId = packet.ReadInt();
            var domain = packet.ReadInt();

            if (requestId <= 0 || roomId <= 0 || entityId <= 0 || packet.HasDataRemaining()) {
                return false;
            }

            request = new(requestId, (uint)roomId, target, entityId, domain);

            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException) {
            return false;
        }
    }
}
