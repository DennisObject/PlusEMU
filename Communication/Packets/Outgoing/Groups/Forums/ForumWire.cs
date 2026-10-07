using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Outgoing.Groups.Forums
{
    internal static class ForumWire
    {
        public static void Forum(IOutgoingPacket packet, GroupForumSnapshot forum)
        {
            packet.WriteInteger(forum.Id);
            packet.WriteString(forum.Name);
            packet.WriteString(forum.Description);
            packet.WriteString(forum.Badge);
            packet.WriteInteger(forum.Threads);
            packet.WriteInteger(0);
            packet.WriteInteger(forum.Messages);
            packet.WriteInteger(forum.Unread);
            packet.WriteInteger(forum.LastMessageId);
            packet.WriteInteger(forum.LastAuthorId);
            packet.WriteString(forum.LastAuthorName);
            packet.WriteInteger(forum.LastPostSecondsAgo);
        }

        public static void Thread(IOutgoingPacket packet, ForumThreadSnapshot thread)
        {
            packet.WriteInteger(thread.Id);
            packet.WriteInteger(thread.AuthorId);
            packet.WriteString(thread.AuthorName);
            packet.WriteString(thread.Title);
            packet.WriteBoolean(thread.Pinned);
            packet.WriteBoolean(thread.Locked);
            packet.WriteInteger(thread.CreatedSecondsAgo);
            packet.WriteInteger(thread.Messages);
            packet.WriteInteger(thread.Unread);
            packet.WriteInteger(thread.LastMessageId);
            packet.WriteInteger(thread.LastAuthorId);
            packet.WriteString(thread.LastAuthorName);
            packet.WriteInteger(thread.LastPostSecondsAgo);
            packet.WriteByte(thread.State);
            packet.WriteInteger(thread.ModeratorId);
            packet.WriteString(thread.ModeratorName);
            packet.WriteInteger(thread.ModeratedSecondsAgo);
        }

        public static void Message(IOutgoingPacket packet, ForumMessageSnapshot message)
        {
            packet.WriteInteger(message.Id);
            packet.WriteInteger(message.Index);
            packet.WriteInteger(message.AuthorId);
            packet.WriteString(message.AuthorName);
            packet.WriteString(message.AuthorFigure);
            packet.WriteInteger(message.CreatedSecondsAgo);
            packet.WriteString(message.Body);
            packet.WriteByte(message.State);
            packet.WriteInteger(message.ModeratorId);
            packet.WriteString(message.ModeratorName);
            packet.WriteInteger(message.ModeratedSecondsAgo);
            packet.WriteInteger(message.AuthorPosts);
        }
    }
}
