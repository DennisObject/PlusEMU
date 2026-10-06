using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Sound;

public class SoundSettingsComposer : IServerPacket
{
    private readonly int[] _volumes;
    private readonly bool _chatPreference;
    private readonly bool _invitesStatus;
    private readonly bool _focusPreference;
    private readonly int _friendBarState;

    public uint MessageId => ServerPacketHeader.SoundSettingsComposer;

    public SoundSettingsComposer(IEnumerable<int> volumes, bool chatPreference, bool invitesStatus, bool focusPreference, int friendBarState)
    {
        _volumes = volumes.ToArray();
        _chatPreference = chatPreference;
        _invitesStatus = invitesStatus;
        _focusPreference = focusPreference;
        _friendBarState = friendBarState;
    }

    public void Compose(IOutgoingPacket packet)
    {
        // UserSettingsParser reads three volumes, three preference bytes, two ints
        // and three bools before its optional tail. One leftover byte makes that
        // tail throw, and the client drops the whole packet.
        for (var index = 0; index < 3; index++)
        {
            packet.WriteInteger(index < _volumes.Length ? _volumes[index] : 0);
        }

        packet.WriteBoolean(_chatPreference);
        packet.WriteBoolean(_invitesStatus);
        packet.WriteBoolean(_focusPreference);
        packet.WriteInteger(_friendBarState);
        packet.WriteInteger(0);
        packet.WriteBoolean(true);
        packet.WriteBoolean(true);
        packet.WriteBoolean(true);

    }
}
