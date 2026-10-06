using System.Data;
using Dapper;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Communication.Packets.Outgoing.Sound;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Core.FigureData;
using Plus.Database;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Rooms.Chat.Styles;
using Plus.HabboHotel.Users.Messenger.FriendBar;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities;

using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Users;

public sealed record FigureUpdateRequest(string Gender, string Figure);
public sealed record SoundVolumeRequest(int System, int Furni, int Music);

public interface IUserProfileService
{
    void ShowUserObject(GameClient session);
    Task SetChatPreference(GameClient session, bool enabled);
    Task SetMessengerInvitePreference(GameClient session, bool enabled);
    Task SetSoundVolumes(GameClient session, SoundVolumeRequest request);
    void UpdateFigure(GameClient session, FigureUpdateRequest request);
    void ChangeMotto(GameClient session, string motto);
    void SetFocusPreference(GameClient session, bool enabled);
    Task SetChatStylePreference(GameClient session, int bubbleId);
    void SetFriendBarState(GameClient session, int state);
}

public sealed class UserProfileService(
    IFigureDataManager figureManager,
    IAchievementManager achievementManager,
    IQuestManager questManager,
    IWordFilterManager wordFilterManager,
    IDatabase database, TimeProvider clock, IChatStyleManager styles,
    IRewardTrackManager rewardTrackManager, IAccountSessionGate accountSessionGate) : IUserProfileService
{
    public void ShowUserObject(GameClient session)
    {
        session.Send(new UserObjectComposer(UserObjectSnapshot.Capture(session.GetHabbo())));
        session.Send(new UserPerksComposer());
    }

    public async Task SetChatPreference(GameClient session, bool enabled)
    {
        var habbo = session.GetHabbo();
        using var connection = database.Connection();
        var updated = await connection.ExecuteAsync(
            "UPDATE users_settings SET chat_preference = @enabled WHERE user_id = @userId LIMIT 1",
            new { enabled, userId = habbo.Id });
        if (updated != 1)
            throw new DBConcurrencyException($"Settings for user {habbo.Id} no longer exist.");
        habbo.ChatPreference = enabled;
    }

    public async Task SetMessengerInvitePreference(GameClient session, bool enabled)
    {
        var habbo = session.GetHabbo();
        using var connection = database.Connection();
        var updated = await connection.ExecuteAsync(
            "UPDATE users_settings SET ignore_invites = @enabled WHERE user_id = @userId LIMIT 1",
            new { enabled, userId = habbo.Id });
        if (updated != 1)
            throw new DBConcurrencyException($"Settings for user {habbo.Id} no longer exist.");
        habbo.AllowMessengerInvites = enabled;
    }

    public async Task SetSoundVolumes(GameClient session, SoundVolumeRequest request)
    {
        var habbo = session.GetHabbo();
        var volumes = new[] { NormalizeVolume(request.System), NormalizeVolume(request.Furni), NormalizeVolume(request.Music) };
        using var connection = database.Connection();
        var updated = await connection.ExecuteAsync(
            "UPDATE users_settings SET volume = @volume WHERE user_id = @userId LIMIT 1",
            new { volume = string.Join(",", volumes), userId = habbo.Id });
        if (updated != 1)
            throw new DBConcurrencyException($"Settings for user {habbo.Id} no longer exist.");
        habbo.ClientVolume = volumes.ToList();
    }

    private static int NormalizeVolume(int value) => value is >= 0 and <= 100 ? value : 100;

    public void UpdateFigure(GameClient session, FigureUpdateRequest request)
    {
        var habbo = session.GetHabbo();
        using var account = accountSessionGate.Enter(habbo.Id);
        var gender = request.Gender.ToUpper();
        var look = figureManager.ProcessFigure(request.Figure, gender, habbo.Clothing.GetClothingParts,
            ClubAccess.LevelFor(habbo.Access));
        if (look == habbo.Look) return;
        var now = clock.GetUtcNow();
        if (habbo.LastClothingUpdatedAt is { } lastUpdate && now - lastUpdate <= TimeSpan.FromSeconds(2))
        {
            habbo.ClothingUpdateWarnings++;
            if (habbo.ClothingUpdateWarnings >= 25) habbo.SessionClothingBlocked = true;
            return;
        }
        if (habbo.SessionClothingBlocked) return;
        if (gender is not ("M" or "F"))
        {
            habbo.LastClothingUpdatedAt = now;
            session.Send(new BroadcastMessageAlertComposer("Sorry, you chose an invalid gender."));
            return;
        }
        var liveLook = figureManager.FilterFigure(look);

        using (var connection = database.Connection())
        {
            var updated = connection.Execute("UPDATE users SET look=@look, gender=@gender WHERE id=@userId LIMIT 1",
                new { look, gender, userId = habbo.Id });
            if (updated != 1)
                throw new DBConcurrencyException($"User {habbo.Id} no longer exists.");
        }

        habbo.LastClothingUpdatedAt = now;
        questManager.ProgressUserQuest(session, QuestType.ProfileChangeLook);
        habbo.Look = liveLook;
        habbo.Gender = gender.ToLower();
        rewardTrackManager.Progress(session, RewardTrackActions.ChangeFigure);
        achievementManager.ProgressAchievement(session, "ACH_AvatarLooks", 1);
        session.Send(new AvatarAspectUpdateComposer(look, gender));
        if (habbo.Look.Contains("ha-1006")) questManager.ProgressUserQuest(session, QuestType.WearHat);
        if (!habbo.InRoom) return;
        var roomUser = habbo.CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (roomUser == null) return;
        session.Send(new UserChangeComposer(AvatarChangeSnapshot.Capture(roomUser, true)));
        habbo.CurrentRoom.SendPacket(new UserChangeComposer(AvatarChangeSnapshot.Capture(roomUser, false)));
    }

    public void ChangeMotto(GameClient session, string motto)
    {
        var habbo = session.GetHabbo();
        using var account = accountSessionGate.Enter(habbo.Id);
        if (habbo.TimeMuted > 0)
        {
            session.SendNotification("Oops, you're currently muted - you cannot change your motto.");
            return;
        }
        var now = clock.GetUtcNow();
        if (habbo.LastMottoUpdatedAt is { } lastUpdate && now - lastUpdate <= TimeSpan.FromSeconds(2))
        {
            habbo.MottoUpdateWarnings++;
            if (habbo.MottoUpdateWarnings >= 25) habbo.SessionMottoBlocked = true;
            return;
        }
        if (habbo.SessionMottoBlocked) return;
        var newMotto = StringCharFilter.Escape(motto.Trim());
        if (newMotto.Length > 38) newMotto = newMotto[..38];
        if (newMotto == habbo.Motto) return;
        if (!habbo.Access.Can(PermissionKeys.ChatFilterBypass)) newMotto = wordFilterManager.CheckMessage(newMotto);
        using (var connection = database.Connection())
        {
            var updated = connection.Execute("UPDATE users SET motto=@motto WHERE id=@userId LIMIT 1",
                new { userId = habbo.Id, motto = newMotto });
            if (updated != 1)
                throw new DBConcurrencyException($"User {habbo.Id} no longer exists.");
        }
        habbo.LastMottoUpdatedAt = now;
        habbo.Motto = newMotto;
        rewardTrackManager.Progress(session, RewardTrackActions.ChangeMotto);
        questManager.ProgressUserQuest(session, QuestType.ProfileChangeMotto);
        achievementManager.ProgressAchievement(session, "ACH_Motto", 1);
        if (!habbo.InRoom) return;
        var room = habbo.CurrentRoom;
        if (room == null) return;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (user?.GetClient() == null) return;
        room.SendPacket(new UserChangeComposer(AvatarChangeSnapshot.Capture(user, false)));
    }

    public Task SetChatStylePreference(GameClient session, int bubbleId)
    {
        var habbo = session.GetHabbo();
        if (bubbleId != 0 && (!styles.TryGetStyle(bubbleId, out var style) || !style.CanUse(habbo.Access)))
            return Task.CompletedTask;
        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed) return Task.CompletedTask;
            using var connection = database.Connection();
            var updated = connection.Execute("UPDATE users SET bubble_id=@bubbleId WHERE id=@userId LIMIT 1",
                new { bubbleId, userId = habbo.Id });
            if (updated != 1)
                throw new DBConcurrencyException($"User {habbo.Id} no longer exists.");
            habbo.CustomBubbleId = bubbleId;
        }
        return Task.CompletedTask;
    }

    public void SetFriendBarState(GameClient session, int state)
    {
        var habbo = session.GetHabbo();
        habbo.FriendbarState = FriendBarStateUtility.GetEnum(state);
        session.Send(new SoundSettingsComposer(habbo.ClientVolume, habbo.ChatPreference, habbo.AllowMessengerInvites,
            habbo.FocusPreference, FriendBarStateUtility.GetInt(habbo.FriendbarState)));
    }

    public void SetFocusPreference(GameClient session, bool enabled)
    {
        var habbo = session.GetHabbo();
        using var connection = database.Connection();
        connection.Execute("UPDATE users_settings SET focus_preference=@enabled WHERE user_id=@userId LIMIT 1",
            new { enabled, userId = habbo.Id });
        habbo.FocusPreference = enabled;
    }
}
