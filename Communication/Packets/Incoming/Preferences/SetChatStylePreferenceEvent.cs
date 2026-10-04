using Plus.HabboHotel.Rooms.Chat.Styles;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Preferences;

internal class SetChatStylePreferenceEvent(IChatStyleManager styles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var chatBubbleId = packet.ReadInt();

        if (chatBubbleId != 0 && (!styles.TryGetStyle(chatBubbleId, out var style) || !style.CanUse(session.GetHabbo().Access)))
            return Task.CompletedTask;
        session.GetHabbo().CustomBubbleId = chatBubbleId;
        session.GetHabbo().SaveChatBubble(chatBubbleId.ToString());

        return Task.CompletedTask;
    }
}