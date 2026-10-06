using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Sound;

internal class SetSoundSettingsEvent(IUserProfileService profiles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        profiles.SetSoundVolumes(session, new SoundVolumeRequest(packet.ReadInt(), packet.ReadInt(), packet.ReadInt()));
}
