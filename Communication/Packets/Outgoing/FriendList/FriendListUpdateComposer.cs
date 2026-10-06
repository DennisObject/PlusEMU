using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class FriendListUpdateComposer : IServerPacket
{
    private readonly ImmutableArray<MessengerBuddyModification> _modifications;

    public uint MessageId => ServerPacketHeader.FriendListUpdateComposer;

    public FriendListUpdateComposer(IReadOnlyList<MessengerBuddyModification> modifications)
    {
        _modifications = modifications.ToImmutableArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(0);
        packet.WriteInteger(_modifications.Length);
        foreach (var modification in _modifications)
        {
            packet.WriteInteger((int)modification.Type);
            if (modification.Type == BuddyModificationType.Added || modification.Type == BuddyModificationType.Updated)
                MessengerBuddyWire.Write(packet, modification.Buddy!);
            else
                packet.WriteInteger(modification.BuddyId);
        }
    }
}