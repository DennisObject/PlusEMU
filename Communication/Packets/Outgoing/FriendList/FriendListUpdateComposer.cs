using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class FriendListUpdateComposer(IReadOnlyList<MessengerBuddyModification> modifications) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.FriendListUpdateComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(0);
        packet.WriteInteger(modifications.Count);
        foreach (var modification in modifications)
        {
            packet.WriteInteger((int)modification.Type);
            if (modification.Type == BuddyModificationType.Added || modification.Type == BuddyModificationType.Updated)
                MessengerBuddyWire.Write(packet, modification.Buddy!);
            else
                packet.WriteInteger(modification.BuddyId);
        }
    }
}