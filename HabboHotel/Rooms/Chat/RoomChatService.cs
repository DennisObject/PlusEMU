using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Rooms.Chat.Logs;
using Plus.HabboHotel.Rooms.Chat.Styles;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms.Chat;

public interface IRoomChatService
{
    Task Chat(GameClient session, string message, int colour);
    Task Shout(GameClient session, string message, int colour);
    Task Whisper(GameClient session, string parameters, int colour);
}

public sealed class RoomChatService(
    IChatStyleManager chatStyleManager,
    IChatlogManager chatlogManager,
    IWordFilterManager wordFilterManager,
    ICommandManager commandManager,
    IModerationManager moderationManager,
    ISettingsManager settingsManager,
    IQuestManager questManager,
    IRewardTrackManager rewardTrackManager,
    TimeProvider clock) : IRoomChatService
{
    public Task Chat(GameClient session, string message, int colour) => PublicChat(session, message, colour, false);

    public Task Shout(GameClient session, string message, int colour) => PublicChat(session, message, colour, true);

    private async Task PublicChat(GameClient session, string message, int colour, bool shout)
    {
        var habbo = session.GetHabbo();
        if (!habbo.InRoom)
            return;
        var room = habbo.CurrentRoom;
        if (room == null)
            return;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (user == null)
            return;
        var now = clock.GetUtcNow();
        message = StringCharFilter.Escape(message);
        if (message.Length > 100)
            message = message[..100];
        if (habbo.CustomBubbleId != 0)
            colour = habbo.CustomBubbleId;
        if (!chatStyleManager.TryGetStyle(colour, out var style) || !style.CanUse(habbo.Access))
            colour = 0;
        if (shout)
            user.LastBubble = colour;
        else
            user.UnIdle();
        if (now.ToUnixTimeSeconds() < habbo.FloodTime && habbo.FloodTime != 0)
            return;
        if (habbo.TimeMuted > 0)
        {
            session.Send(new MutedComposer(habbo.TimeMuted));
            return;
        }
        if (!habbo.Access.Can(PermissionKeys.RoomIgnoreMute) && room.CheckMute(session))
        {
            session.SendWhisper("Oops, you're currently muted.");
            return;
        }
        if (!shout)
            user.LastBubble = colour;
        if (!habbo.Access.Can(PermissionKeys.ModerationTool) && user.IncrementAndCheckFlood(out var muteTime))
        {
            session.Send(new FloodControlComposer(muteTime));
            return;
        }

        chatlogManager.StoreChatlog(new(habbo.Id, room.Id, message, now, habbo, room));
        if (message.StartsWith(":", StringComparison.CurrentCulture) && await commandManager.Parse(session, message))
            return;
        if (wordFilterManager.CheckBannedWords(message))
        {
            habbo.BannedPhraseCount++;
            if (habbo.BannedPhraseCount >= Convert.ToInt32(settingsManager.TryGetValue("room.chat.filter.banned_phrases.chances")))
            {
                await moderationManager.BanUser("System", ModerationBanType.Username, habbo.Username,
                    $"Spamming banned phrases ({message})", now.ToUnixTimeSeconds() + 78892200);
                session.Disconnect();
                return;
            }
            session.Send(shout
                ? new ShoutComposer(user.VirtualId, message, 0, colour)
                : new ChatComposer(user.VirtualId, message, 0, colour));
            return;
        }
        if (!habbo.Access.Can(PermissionKeys.ChatFilterBypass))
            message = wordFilterManager.CheckMessage(message);
        questManager.ProgressUserQuest(session, QuestType.SocialChat);
        if (shout)
            user.UnIdle();
        user.OnChat(user.LastBubble, message, shout);
        if (room.GetRoomUserManager().GetRoomUsers().Count > 1)
            rewardTrackManager.Progress(session, RewardTrackActions.ChatWithSomeone);
    }

    public async Task Whisper(GameClient session, string parameters, int colour)
    {
        var habbo = session.GetHabbo();
        if (!habbo.InRoom)
            return;
        var room = habbo.CurrentRoom;
        if (room == null)
            return;
        var now = clock.GetUtcNow();
        if (!habbo.Access.Can(PermissionKeys.ModerationTool) && room.CheckMute(session))
        {
            session.SendWhisper("Oops, you're currently muted.");
            return;
        }
        if (now.ToUnixTimeSeconds() < habbo.FloodTime && habbo.FloodTime != 0)
            return;
        var toUser = parameters.Split(' ')[0];
        var message = parameters.Substring(toUser.Length + 1);
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (user == null)
            return;
        var recipient = room.GetRoomUserManager().GetRoomUserByHabbo(toUser);
        if (recipient == null)
            return;
        if (habbo.TimeMuted > 0)
        {
            session.Send(new MutedComposer(habbo.TimeMuted));
            return;
        }
        if (!habbo.Access.Can(PermissionKeys.ChatFilterBypass))
            message = wordFilterManager.CheckMessage(message);
        if (habbo.CustomBubbleId != 0)
            colour = habbo.CustomBubbleId;
        if (!chatStyleManager.TryGetStyle(colour, out var style) || !style.CanUse(habbo.Access))
            colour = 0;
        user.LastBubble = colour;
        if (!habbo.Access.Can(PermissionKeys.ModerationTool) && user.IncrementAndCheckFlood(out var muteTime))
        {
            session.Send(new FloodControlComposer(muteTime));
            return;
        }
        if (!recipient.GetClient().GetHabbo().ReceiveWhispers && !habbo.Access.Can(PermissionKeys.RoomWhisperOverride))
        {
            session.SendWhisper("Oops, this user has their whispers disabled!");
            return;
        }
        chatlogManager.StoreChatlog(new(habbo.Id, room.Id, $"<Whisper to {toUser}>: {message}", now, habbo, room));
        if (wordFilterManager.CheckBannedWords(message))
        {
            habbo.BannedPhraseCount++;
            if (habbo.BannedPhraseCount >= Convert.ToInt32(settingsManager.TryGetValue("room.chat.filter.banned_phrases.chances")))
            {
                await moderationManager.BanUser("System", ModerationBanType.Username, habbo.Username,
                    $"Spamming banned phrases ({message})", now.ToUnixTimeSeconds() + 78892200);
                session.Disconnect();
                return;
            }
            session.Send(new WhisperComposer(user.VirtualId, message, 0, user.LastBubble));
            return;
        }
        questManager.ProgressUserQuest(session, QuestType.SocialChat);
        user.UnIdle();
        user.GetClient().Send(new WhisperComposer(user.VirtualId, message, 0, user.LastBubble));
        if (!recipient.IsBot && recipient.UserId != user.UserId
            && !recipient.GetClient().GetHabbo().IgnoresComponent.IsIgnored(habbo.Id))
            recipient.GetClient().Send(new WhisperComposer(user.VirtualId, message, 0, user.LastBubble));
        foreach (var notifiable in room.GetRoomUserManager().GetRoomUsersWithPermission(PermissionKeys.StaffReceiveAlerts))
        {
            if (notifiable != null && notifiable.HabboId != recipient.HabboId && notifiable.HabboId != user.HabboId
                && notifiable.GetClient() != null && notifiable.GetClient().GetHabbo() != null
                && !notifiable.GetClient().GetHabbo().IgnorePublicWhispers)
                notifiable.GetClient().Send(new WhisperComposer(user.VirtualId, $"[Whisper to {toUser}] {message}", 0,
                    user.LastBubble));
        }
        if (room.GetRoomUserManager().GetRoomUsers().Count > 1)
            rewardTrackManager.Progress(session, RewardTrackActions.ChatWithSomeone);
    }
}
