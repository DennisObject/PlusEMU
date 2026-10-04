using Dapper;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Core.FigureData;
using Plus.Database;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Subscriptions;
using Plus.Utilities;

namespace Plus.HabboHotel.Users;

public sealed record FigureUpdateRequest(string Gender, string Figure);

public interface IUserProfileService
{
    void UpdateFigure(GameClient session, FigureUpdateRequest request);
    void ChangeMotto(GameClient session, string motto);
    void SetFocusPreference(GameClient session, bool enabled);
}

public sealed class UserProfileService(
    IFigureDataManager figureManager,
    IAchievementManager achievementManager,
    IQuestManager questManager,
    IWordFilterManager wordFilterManager,
    IDatabase database) : IUserProfileService
{
    public void UpdateFigure(GameClient session, FigureUpdateRequest request)
    {
        var habbo = session.GetHabbo();
        var gender = request.Gender.ToUpper();
        var look = figureManager.ProcessFigure(request.Figure, gender, habbo.Clothing.GetClothingParts,
            ClubAccess.LevelFor(habbo.Access));
        if (look == habbo.Look) return;
        if ((DateTime.Now - habbo.LastClothingUpdateTime).TotalSeconds <= 2.0)
        {
            habbo.ClothingUpdateWarnings++;
            if (habbo.ClothingUpdateWarnings >= 25) habbo.SessionClothingBlocked = true;
            return;
        }
        if (habbo.SessionClothingBlocked) return;
        habbo.LastClothingUpdateTime = DateTime.Now;
        if (gender is not ("M" or "F"))
        {
            session.Send(new BroadcastMessageAlertComposer("Sorry, you chose an invalid gender."));
            return;
        }

        questManager.ProgressUserQuest(session, QuestType.ProfileChangeLook);
        habbo.Look = figureManager.FilterFigure(look);
        habbo.Gender = gender.ToLower();
        RewardTrackManager.Current?.Progress(session, RewardTrackActions.ChangeFigure);
        using (var connection = database.Connection())
            connection.Execute("UPDATE users SET look=@look, gender=@gender WHERE id=@userId LIMIT 1",
                new { look, gender, userId = habbo.Id });
        achievementManager.ProgressAchievement(session, "ACH_AvatarLooks", 1);
        session.Send(new AvatarAspectUpdateComposer(look, gender));
        if (habbo.Look.Contains("ha-1006")) questManager.ProgressUserQuest(session, QuestType.WearHat);
        if (!habbo.InRoom) return;
        var roomUser = habbo.CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (roomUser == null) return;
        session.Send(new UserChangeComposer(roomUser, true));
        habbo.CurrentRoom.SendPacket(new UserChangeComposer(roomUser, false));
    }

    public void ChangeMotto(GameClient session, string motto)
    {
        var habbo = session.GetHabbo();
        if (habbo.TimeMuted > 0)
        {
            session.SendNotification("Oops, you're currently muted - you cannot change your motto.");
            return;
        }
        if ((DateTime.Now - habbo.LastMottoUpdateTime).TotalSeconds <= 2.0)
        {
            habbo.MottoUpdateWarnings++;
            if (habbo.MottoUpdateWarnings >= 25) habbo.SessionMottoBlocked = true;
            return;
        }
        if (habbo.SessionMottoBlocked) return;
        habbo.LastMottoUpdateTime = DateTime.Now;
        var newMotto = StringCharFilter.Escape(motto.Trim());
        if (newMotto.Length > 38) newMotto = newMotto[..38];
        if (newMotto == habbo.Motto) return;
        if (!habbo.Access.Can(PermissionKeys.ChatFilterBypass)) newMotto = wordFilterManager.CheckMessage(newMotto);
        habbo.Motto = newMotto;
        using (var connection = database.Connection())
            connection.Execute("UPDATE users SET motto=@motto WHERE id=@userId LIMIT 1", new { userId = habbo.Id, motto = newMotto });
        RewardTrackManager.Current?.Progress(session, RewardTrackActions.ChangeMotto);
        questManager.ProgressUserQuest(session, QuestType.ProfileChangeMotto);
        achievementManager.ProgressAchievement(session, "ACH_Motto", 1);
        if (!habbo.InRoom) return;
        var room = habbo.CurrentRoom;
        if (room == null) return;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (user?.GetClient() == null) return;
        room.SendPacket(new UserChangeComposer(user, false));
    }

    public void SetFocusPreference(GameClient session, bool enabled)
    {
        var habbo = session.GetHabbo();
        habbo.FocusPreference = enabled;
        using var connection = database.Connection();
        connection.Execute("UPDATE users_settings SET focus_preference=@enabled WHERE user_id=@userId LIMIT 1",
            new { enabled, userId = habbo.Id });
    }
}
