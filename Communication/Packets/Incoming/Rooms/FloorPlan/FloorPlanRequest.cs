using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.FloorPlan;

internal static class FloorPlanRequest
{
    public readonly record struct Body(
        string Map,
        bool DoorFieldsPresent,
        bool WallHeightPresent,
        FloorPlanSave.Layout Requested);

    public static Body Read(IIncomingPacket packet)
    {
        var map = packet.ReadString();

        if (!packet.HasDataRemaining()) {
            return new Body(map, false, false, default);
        }

        var requested = new FloorPlanSave.Layout(
            packet.ReadInt(),
            packet.ReadInt(),
            packet.ReadInt(),
            packet.ReadInt(),
            packet.ReadInt(),
            -1);
        // The wall-height int is absent when class_2506 was given -1. Do not read past the five ints.
        var wallHeightPresent = packet.Buffer.Length >= sizeof(int);

        if (wallHeightPresent) {
            requested = requested with { WallHeight = packet.ReadInt() };
        }

        return new Body(map, true, wallHeightPresent, requested);
    }
}
