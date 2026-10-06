using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Data.Moodlight;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Moodlight;

public class MoodlightConfigComposer : IServerPacket
{
    private readonly MoodlightConfigSnapshot _snapshot;

    public uint MessageId => ServerPacketHeader.MoodlightConfigComposer;

    public MoodlightConfigComposer(MoodlightConfigSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_snapshot.Presets.Length);
        packet.WriteInteger(_snapshot.CurrentPreset);
        var i = 1;

        foreach (var preset in _snapshot.Presets) {
            packet.WriteInteger(i);
            packet.WriteInteger(preset.BackgroundOnly ? 2 : 1);
            packet.WriteString(preset.ColorCode);
            packet.WriteInteger(preset.ColorIntensity);
            i++;
        }
    }
}
