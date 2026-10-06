using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Moodlight;

internal class MoodlightUpdateEvent(IMoodlightService moodlight) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var preset = packet.ReadInt();
        var backgroundMode = packet.ReadInt();
        var colorCode = packet.ReadString();
        var intensity = packet.ReadInt();
        moodlight.UpdatePreset(room, session, new(preset, colorCode, intensity, backgroundMode));

        return Task.CompletedTask;
    }
}
