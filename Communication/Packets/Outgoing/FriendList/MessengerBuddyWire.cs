using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Communication.Packets.Outgoing.FriendList;

// The buddy entry layout shared by the friend list and the friend list updates.
internal static class MessengerBuddyWire
{
    public static void Write(IOutgoingPacket message, MessengerBuddySnapshot buddy)
    {
        message.WriteInteger(buddy.Id);
        message.WriteString(buddy.Username);
        message.WriteInteger(buddy.Gender);
        message.WriteBoolean(buddy.Online);
        message.WriteBoolean(buddy.ShowInRoom);
        message.WriteString(buddy.Look);
        message.WriteInteger(0); // categoryid
        message.WriteString(buddy.Motto);
        message.WriteString(string.Empty); // Facebook username
        message.WriteString(string.Empty);
        message.WriteBoolean(true); // Allows offline messaging
        message.WriteBoolean(false); // ?
        message.WriteBoolean(false); // Uses phone
        message.WriteShort(buddy.Relationship);
    }
}
