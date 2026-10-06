using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;

public class AvatarEffectAddedComposer : IServerPacket
{
    private readonly int _spriteId;
    private readonly int _duration;
    public uint MessageId => ServerPacketHeader.AvatarEffectAddedComposer;

    public AvatarEffectAddedComposer(int spriteId, int duration)
    {
        _spriteId = spriteId;
        _duration = duration;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_spriteId);
        packet.WriteInteger(0); //Types
        packet.WriteInteger(_duration);
        packet.WriteBoolean(false); //Permanent
    }
}