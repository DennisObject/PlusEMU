using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Subscriptions;

namespace Plus.HabboHotel.Rooms;

public interface IRoomAvatarActionService
{
    void Move(GameClient session, int x, int y);
    void GiveHandItem(Room room, GameClient session, int userId);
    void PerformAction(Room room, GameClient session, int action);
    void Dance(Room room, GameClient session, int danceId);
    void SetPosture(GameClient session, int posture);
    void LookTo(Room room, GameClient session, int x, int y);
    void SetTyping(GameClient session, bool typing);
    void ApplySign(Room room, GameClient session, int signId);
}

public sealed class RoomAvatarActionService(TimeProvider clock, IQuestManager questManager, IRewardTrackManager rewards) : IRoomAvatarActionService
{
    private static readonly TimeSpan SignDuration = TimeSpan.FromSeconds(5);

    public void Move(GameClient session, int x, int y)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;

        if (!habbo.InRoom || room == null) {
            return;
        }

        var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);

        if (user == null || !room.GetGameMap().ValidTile(x, y)) {
            return;
        }

        if (!user.IsBot) {
            room.GetWired().Dispatch(new(WiredEventKind.ClickTile) { Actor = user, X = x, Y = y });
        }

        if (!user.CanWalk) {
            return;
        }

        if (room.UsesV2Movement) {
            if (!user.SetStep && x == user.X && y == user.Y) {
                return;
            }

            user.MoveTo(x, y);

            return;
        }

        if (x == user.X && y == user.Y) {
            return;
        }

        if (user.RidingHorse) {
            var horse = room.GetRoomUserManager().GetRoomUserByVirtualId(user.HorseId);

            if (horse != null) {
                horse.MoveTo(x, y);
            }
        }

        user.MoveTo(x, y);
    }

    public void GiveHandItem(Room room, GameClient session, int userId)
    {
        var habbo = session.GetHabbo();

        if (!ReferenceEquals(habbo.CurrentRoom, room)) {
            return;
        }

        var users = room.GetRoomUserManager();
        var actor = users.GetRoomUserByHabbo(habbo.Id);
        var target = users.GetRoomUserByHabbo(userId);

        if (actor == null || target == null) {
            return;
        }

        if ((Math.Abs(actor.X - target.X) >= 3 || Math.Abs(actor.Y - target.Y) >= 3)
            && !habbo.Access.Can(PermissionKeys.ModerationTool)) {
            return;
        }

        if (actor.CarryItemId <= 0 || actor.CarryTimer <= 0) {
            return;
        }

        if (actor.CarryItemId == 8) {
            questManager.ProgressUserQuest(session, QuestType.GiveCoffee);
        }

        target.CarryItem(actor.CarryItemId);
        actor.CarryItem(0);
        target.DanceId = 0;
    }

    public void PerformAction(Room room, GameClient session, int action)
    {
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

        if (user == null) {
            return;
        }

        if (user.DanceId > 0) {
            user.DanceId = 0;
        }

        if (session.GetHabbo().Effects.CurrentEffect > 0) {
            room.SendPacket(new AvatarEffectComposer(user.VirtualId, 0));
        }

        user.UnIdle();
        room.SendPacket(new ActionComposer(user.VirtualId, action));

        if (action == 5) {
            user.IsAsleep = true;
            room.SendPacket(new SleepComposer(user.VirtualId, true));
        }

        var wiredAction = action switch
        {
            1 => WiredAvatarAction.Wave,
            2 => WiredAvatarAction.Kiss,
            3 => WiredAvatarAction.Laugh,
            5 => WiredAvatarAction.Relax,
            7 => WiredAvatarAction.ThumbUp,
            _ => (WiredAvatarAction)0
        };

        if (wiredAction != 0) {
            room.GetWired().Dispatch(new(WiredEventKind.AvatarAction)
            { Actor = user, Action = (int)wiredAction, Code = -1 });
        }

        if (action == 1) {
            rewards.Progress(session, RewardTrackActions.Wave);
        }

        questManager.ProgressUserQuest(session, QuestType.SocialWave);
    }

    public void Dance(Room room, GameClient session, int danceId)
    {
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

        if (user == null) {
            return;
        }

        user.UnIdle();

        if (danceId < 0 || danceId > 4 || danceId > 1 && ClubAccess.LevelFor(session.GetHabbo().Access) == 0) {
            danceId = 0;
        }

        if (danceId > 0 && user.CarryItemId > 0) {
            user.CarryItem(0);
        }

        if (session.GetHabbo().Effects.CurrentEffect > 0) {
            room.SendPacket(new AvatarEffectComposer(user.VirtualId, 0));
        }

        var previousDance = user.DanceId;
        user.DanceId = danceId;
        room.SendPacket(new DanceComposer(user.VirtualId, danceId));

        if (danceId > 0) {
            room.GetWired().Dispatch(new(WiredEventKind.AvatarAction)
            { Actor = user, Action = (int)WiredAvatarAction.Dance, Code = danceId });
        }

        if (danceId >= 1 && danceId <= 4 && danceId != previousDance) {
            rewards.Progress(session, RewardTrackActions.Dance);
        }

        questManager.ProgressUserQuest(session, QuestType.SocialDance);

        if (room.GetRoomUserManager().GetRoomUsers().Count > 19) {
            questManager.ProgressUserQuest(session, QuestType.MassDance);
        }
    }

    public void SetPosture(GameClient session, int posture)
    {
        if (!session.GetHabbo().InRoom || posture is not (0 or 1)) {
            return;
        }

        var room = session.GetHabbo().CurrentRoom;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

        if (user == null || user.Statusses.ContainsKey("lie") || user.IsLying || user.RidingHorse || user.IsWalking) {
            return;
        }

        var wasSitting = user.IsSitting;

        if (posture == 1 && !user.Statusses.ContainsKey("sit")) {
            if (user.RotBody % 2 != 0) {
                user.RotBody--;
            }

            user.Statusses.Add("sit", "1.0");
            user.Z -= 0.35;
            user.IsSitting = true;
            user.UpdateNeeded = true;
        }
        else if (posture == 0 && user.IsSitting) {
            user.Z += 0.35;
            user.Statusses.Remove("sit");
            user.Statusses.Remove("1.0");
            user.IsSitting = false;
            user.UpdateNeeded = true;
        }

        if (wasSitting != user.IsSitting) {
            room.GetWired().Dispatch(new(WiredEventKind.AvatarAction)
            {
                Actor = user,
                Action = (int)(user.IsSitting ? WiredAvatarAction.Sit : WiredAvatarAction.Stand),
                Code = -1
            });
        }
    }

    public void LookTo(Room room, GameClient session, int x, int y)
    {
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

        if (user == null || user.IsAsleep) {
            return;
        }

        user.UnIdle();

        if (x == user.X && y == user.Y || user.IsWalking || user.RidingHorse) {
            return;
        }

        var rot = Rotation.Calculate(user.X, user.Y, x, y);
        user.SetRot(rot, false);
        user.UpdateNeeded = true;
    }

    public void SetTyping(GameClient session, bool typing)
    {
        if (!session.GetHabbo().InRoom) {
            return;
        }

        var room = session.GetHabbo().CurrentRoom;

        if (room == null) {
            return;
        }

        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Username);

        if (user == null) {
            return;
        }

        room.SendPacket(new UserTypingComposer(user.VirtualId, typing));
    }

    public void ApplySign(Room room, GameClient session, int signId)
    {
        if (signId is < 0 or > 17) {
            return;
        }

        var habbo = session.GetHabbo();

        if (habbo.CurrentRoom != room) {
            return;
        }

        var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);

        if (user == null) {
            return;
        }

        var now = clock.GetUtcNow();
        user.UnIdle();
        user.SetStatus("sign", Convert.ToString(signId));
        user.UpdateNeeded = true;
        user.SignExpiresAt = now + SignDuration;
        room.GetWired().Dispatch(new(WiredEventKind.AvatarAction)
        { Actor = user, Action = (int)WiredAvatarAction.Sign, Code = signId });
    }
}
