using Dapper;
using Plus.HabboHotel.GameClients;
using Plus.Core;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class LandingEffects
{
    private readonly Room _room;
    private readonly IDatabase _database;
    private readonly Action<Habbo, uint> _prepareRoom;

    public LandingEffects(Room room, IDatabase database, Action<Habbo, uint>? prepareRoom = null)
    {
        _room = room;
        _database = database;
        _prepareRoom = prepareRoom ?? ((habbo, id) => habbo.PrepareRoom(id, ""));
    }

    public void Apply(RoomUser actor, bool wasLaying)
    {
        var location = new Location(actor.X, actor.Y, actor.Z, actor.Movement.LocationRevision);

        try {
            var items = SurfaceContacts.Of(_room, actor, _room.GetGameMap().GetAllRoomItemForSquare(actor.X, actor.Y));

            foreach (var item in items) {
                if (!ApplyItem(actor, item) || !IsHere(actor, location)) {
                    return;
                }
            }

            if (!actor.IsBot) {
                ApplyGames(actor, location);
            }
        }
        catch (Exception error) {
            ExceptionLogger.LogException(error);
        }
        finally {
            if (!wasLaying && actor.HasStatus("lay") && IsHere(actor, location)) {
                _room.GetWired().Dispatch(new(WiredEventKind.AvatarAction)
                { Actor = actor, Action = (int)WiredAvatarAction.Lay });
            }
        }
    }

    private bool ApplyItem(RoomUser actor, Item item)
    {
        switch (item.Definition.InteractionType) {
            case InteractionType.Banzaigateblue:
            case InteractionType.Banzaigatered:
            case InteractionType.Banzaigategreen:
            case InteractionType.Banzaigateyellow:
                if (!actor.IsBot) {
                    ToggleTeam(actor, item, _room.GetTeamManagerForBanzai(), 32);
                }

                return true;
            case InteractionType.FreezeBlueGate:
            case InteractionType.FreezeRedGate:
            case InteractionType.FreezeGreenGate:
            case InteractionType.FreezeYellowGate:
                if (!actor.IsBot) {
                    ToggleTeam(actor, item, _room.GetTeamManagerForFreeze(), 39);
                }

                return true;
            case InteractionType.Banzaitele:
                if (actor.HasStatus("mv")) {
                    _room.GetGameItemHandler().OnTeleportRoomUserEnter(actor, item);
                }

                return true;
            case InteractionType.Effect:
                return ApplyEffect(actor, item);
            case InteractionType.Arrow:
                return ApplyArrow(actor, item);
            default:
                return true;
        }
    }

    private static void ToggleTeam(RoomUser actor, Item item, TeamManager teams, int offset)
    {
        var effects = actor.GetClient().GetHabbo().Effects;
        var effect = (int)item.Team + offset;

        if (actor.Team == Team.None) {
            if (!teams.CanEnterOnTeam(item.Team)) {
                return;
            }

            actor.Team = item.Team;
            teams.AddUser(actor);

            if (effects.CurrentEffect != effect) {
                effects.ApplyEffect(effect);
            }

            return;
        }

        var otherTeam = actor.Team != item.Team;
        teams.OnUserLeave(actor);
        actor.Team = Team.None;

        if (otherTeam || effects.CurrentEffect == effect) {
            effects.ApplyEffect(0);
        }
    }

    private static bool ApplyEffect(RoomUser actor, Item item)
    {
        if (actor.IsBot) {
            return true;
        }

        var effects = actor.GetClient()?.GetHabbo()?.Effects;

        if (effects == null || item.Definition.EffectId == 0 && effects.CurrentEffect == 0) {
            return false;
        }

        effects.ApplyEffect(item.Definition.EffectId);
        item.LegacyDataString = "1";
        item.UpdateState(false, true);
        item.RequestUpdate(2, true);

        return true;
    }

    private bool ApplyArrow(RoomUser actor, Item item)
    {
        if (actor.GoalX != item.GetX || actor.GoalY != item.GetY) {
            return true;
        }

        var habbo = actor.GetClient()?.GetHabbo();

        if (habbo == null) {
            return true;
        }

        if (!ReferenceEquals(habbo.CurrentRoom, _room)) {
            return false;
        }

        var linked = ReadId("SELECT `tele_two_id` FROM `room_items_tele_links` WHERE `tele_one_id` = @id LIMIT 1", item.Id);
        var target = _room.GetRoomItemHandler().GetItem(linked);
        var roomId = linked == 0 ? 0 : target != null ? _room.RoomId
            : ReadId("SELECT `room_id` FROM `items` WHERE `id` = @id LIMIT 1", linked);

        if (roomId == 0) {
            actor.UnlockWalking();

            return true;
        }

        if (roomId == _room.RoomId) {
            if (target == null) {
                actor.GetClient().SendWhisper("Hey, that arrow is poorly!");
            }
            else {
                _room.GetGameMap().TeleportToItem(actor, target);
            }

            return false;
        }

        if (!actor.IsBot) {
            PrepareRoom(actor, habbo, linked, roomId);
        }

        return false;
    }

    private void PrepareRoom(RoomUser actor, Habbo habbo, uint linked, uint roomId)
    {
        habbo.IsTeleporting = true;
        habbo.TeleportingRoomId = roomId;
        habbo.TeleporterId = linked;
        _room.GetGameMap().Navigation?.Remove(actor);
        _prepareRoom(habbo, roomId);
    }

    private uint ReadId(string sql, uint id)
    {
        using var connection = _database.Connection();

        return connection.QuerySingleOrDefault<uint?>(sql, new { id }) ?? 0;
    }

    private void ApplyGames(RoomUser actor, Location location)
    {
        if (_room.GotSoccer()) {
            _room.GetSoccer().OnUserWalk(actor);
        }

        if (!IsHere(actor, location)) {
            return;
        }

        if (_room.GotBanzai()) {
            _room.GetBanzai().OnUserWalk(actor);
        }

        if (!IsHere(actor, location)) {
            return;
        }

        if (_room.GotFreeze()) {
            _room.GetFreeze().OnUserWalk(actor);
        }
    }

    private bool IsHere(RoomUser actor, Location location)
        => actor.X == location.X && actor.Y == location.Y && actor.Z == location.Z
            && actor.Movement.LocationRevision == location.Revision && actor.Movement.State != NavState.Removing
            && (actor.IsBot || ReferenceEquals(actor.GetClient()?.GetHabbo()?.CurrentRoom, _room));

    private readonly record struct Location(int X, int Y, double Z, long Revision);
}
