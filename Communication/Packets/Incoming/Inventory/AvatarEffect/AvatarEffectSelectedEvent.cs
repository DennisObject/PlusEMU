using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Effects;

namespace Plus.Communication.Packets.Incoming.Inventory.AvatarEffect;

internal class AvatarEffectSelectedEvent(IAvatarEffectService effects) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        effects.Select(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
