using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.Games.Teams;

namespace Plus.HabboHotel.Rooms.Games;

// Tag roles exist only while their pole is attached; ordinary skating remains untagged.
internal sealed class IceTagGame(Room room, IAchievementManager achievements, TimeProvider clock)
{
    private readonly object _sync = new();
    private readonly Dictionary<RoomUser, Visit> _players = [];
    private readonly HashSet<Item> _poles = [];
    private readonly Dictionary<Item, (long Placement, long Movement, DateTimeOffset Until)> _pulses = [];

    internal void RegisterPole(Item item)
    {
        lock (_sync) {
            if (item.Definition.InteractionType == InteractionType.IceTagPole && Owns(item)) {
                _poles.Add(item);
            }
        }
    }

    internal int Update(RoomUser actor, Item? field)
    {
        GameClient? awardClient = null;
        var minutes = 0;
        int effect;

        lock (_sync) {
            if (field == null && !_players.ContainsKey(actor)) {
                return -1;
            }

            if (!Owns(actor) || field != null && (!Owns(field) || field.Definition.InteractionType != InteractionType.IceSkates)) {
                return -1;
            }

            var now = clock.GetUtcNow();
            _players.TryGetValue(actor, out var visit);

            if (field == null || field.Definition.InteractionType != InteractionType.IceSkates || !Owns(field) || actor.Team != Team.None || actor.RidingHorse) {
                if (visit != null) {
                    minutes = Minutes(visit, now);
                    awardClient = actor.GetClient();
                    _players.Remove(actor);
                }

                effect = -1;
            }
            else {
                if (visit == null) {
                    visit = new(field, now);
                    _players.Add(actor, visit);
                }
                else if (!ReferenceEquals(visit.Field, field) || visit.Placement != field.Placement || visit.Movement != field.MovementGeneration) {
                    minutes = Minutes(visit, now);
                    awardClient = actor.GetClient();
                    visit.Field = field;
                    visit.Placement = field.Placement;
                    visit.Movement = field.MovementGeneration;
                    visit.EnteredAt = now;
                }

                if (visit.Tagged && !ValidPole(visit)) {
                    visit.Tagged = false;
                    visit.Pole = null;
                }

                if (!visit.Tagged) {
                    visit.Pole = FreePole();
                    visit.PolePlacement = visit.Pole?.Placement ?? 0;
                    visit.Tagged = visit.Pole != null;
                }

                effect = Effect(actor, visit.Tagged);
            }
        }

        Award(awardClient, minutes);

        return effect;
    }

    internal void LookTo(RoomUser actor, int x, int y)
    {
        RoomUser? target;
        Item? pulse;
        long placement = 0, movement = 0;
        var restoreSource = false;

        lock (_sync) {
            if (!Owns(actor) || !_players.TryGetValue(actor, out var source) || !source.Tagged || !ValidPole(source) || !OnField(actor, source)
                || Math.Max(Math.Abs((long)actor.X - x), Math.Abs((long)actor.Y - y)) != 1) {
                return;
            }

            target = _players.Keys.FirstOrDefault(player => player.X == x && player.Y == y && Owns(player) && OnField(player, _players[player]));

            if (target == null || ReferenceEquals(target, actor) || _players[target].Tagged) {
                return;
            }

            var next = _players[target];
            var free = FreePole();
            pulse = source.Pole;
            next.Pole = free ?? source.Pole;
            next.PolePlacement = next.Pole!.Placement;
            next.Tagged = true;

            if (free == null) {
                source.Tagged = false;
                source.Pole = null;
                restoreSource = true;
            }

            if (pulse != null && Owns(pulse)) {
                placement = pulse.Placement;
                movement = pulse.MovementGeneration;
                _pulses[pulse] = (placement, movement, clock.GetUtcNow().AddSeconds(1));
            }
        }

        if (restoreSource) {
            Apply(actor, Effect(actor, false));
        }

        Apply(target, Effect(target, true));

        if (pulse != null && Owns(pulse) && pulse.Placement == placement && pulse.MovementGeneration == movement) {
            pulse.LegacyDataString = "1";
            pulse.UpdateState();
        }
    }

    internal void Leave(RoomUser actor)
    {
        GameClient? client;
        int minutes;

        lock (_sync) {
            if (!_players.Remove(actor, out var visit)) {
                return;
            }

            client = actor.GetClient();
            minutes = Minutes(visit, clock.GetUtcNow());
        }

        // Registry removal may precede this hook. Never clear a replacement room's effect.
        if (actor.IsAttachedTo(room) && ReferenceEquals(client?.GetHabbo()?.CurrentRoom, room)) {
            client.GetHabbo().Effects?.ApplyEffect(-1);
            actor.CurrentItemEffect = ItemEffectType.None;
        }

        Award(client, minutes);
    }

    internal void Placed(GameClient session, Item field)
    {
        if (field.Definition.InteractionType == InteractionType.IceSkates && Owns(field)
            && session.GetHabbo() is { } habbo && (uint)habbo.Id == field.OwnerId && ReferenceEquals(habbo.CurrentRoom, room)) {
            achievements.ProgressAchievement(session, "ACH_TagA", 1);
        }
    }

    internal void Removed(Item item)
    {
        RoomUser[] players;
        List<RoomUser> untag = [];

        lock (_sync) {
            _pulses.Remove(item);
            _poles.Remove(item);
            players = _players.Where(pair => ReferenceEquals(pair.Value.Field, item)).Select(pair => pair.Key).ToArray();

            foreach (var (actor, visit) in _players) {
                if (ReferenceEquals(visit.Pole, item)) {
                    visit.Pole = null;
                    visit.Tagged = false;
                    untag.Add(actor);
                }
            }
        }

        foreach (var player in players) {
            Leave(player);
        }

        foreach (var player in untag) {
            Apply(player, Effect(player, false));
        }

        if (item.Definition.InteractionType == InteractionType.IceTagPole) {
            item.LegacyDataString = "0";
        }
    }

    internal void Cycle()
    {
        List<(Item Pole, long Placement, long Movement)> reset = [];
        RoomUser[] stale;
        List<RoomUser> untag = [];

        lock (_sync) {
            var now = clock.GetUtcNow();

            foreach (var (pole, pulse) in _pulses.ToArray()) {
                if (!Owns(pole) || pole.Placement != pulse.Placement || pole.MovementGeneration != pulse.Movement || now >= pulse.Until) {
                    _pulses.Remove(pole);

                    if (Owns(pole) && pole.Placement == pulse.Placement && pole.MovementGeneration == pulse.Movement) {
                        reset.Add((pole, pulse.Placement, pulse.Movement));
                    }
                }
            }

            stale = _players.Where(pair => !Owns(pair.Key) || !OnField(pair.Key, pair.Value)).Select(pair => pair.Key).ToArray();
            _poles.RemoveWhere(pole => !Owns(pole));

            foreach (var (actor, visit) in _players) {
                if (visit.Tagged && !ValidPole(visit)) {
                    visit.Tagged = false;
                    visit.Pole = null;

                    if (!stale.Contains(actor)) {
                        untag.Add(actor);
                    }
                }
            }
        }

        foreach (var (pole, placement, movement) in reset) {
            if (Owns(pole) && pole.Placement == placement && pole.MovementGeneration == movement) {
                pole.LegacyDataString = "0";
                pole.UpdateState();
            }
        }

        foreach (var actor in stale) {
            Leave(actor);
        }

        foreach (var actor in untag) {
            Apply(actor, Effect(actor, false));
        }
    }

    private bool Owns(RoomUser actor) => !room.MDisposed && !actor.IsBot && actor.IsAttachedTo(room)
        && ReferenceEquals(room.GetRoomUserManager()?.GetRoomUserByVirtualId(actor.InternalRoomId), actor)
        && ReferenceEquals(actor.GetClient()?.GetHabbo()?.CurrentRoom, room);
    private bool Owns(Item item) => ReferenceEquals(item.GetRoom(), room)
        && ReferenceEquals(room.GetRoomItemHandler()?.GetItem(item.Id), item);
    private bool OnField(RoomUser actor, Visit visit) => actor.Team == Team.None && !actor.RidingHorse && Owns(visit.Field) && visit.Placement == visit.Field.Placement
        && visit.Movement == visit.Field.MovementGeneration
        && (Covers(visit.Field, actor.X, actor.Y) || actor.SetStep && Covers(visit.Field, actor.SetX, actor.SetY));
    private static bool Covers(Item field, int x, int y) => field.GetX == x && field.GetY == y
        || field.GetAffectedTiles.Values.Any(tile => tile.X == x && tile.Y == y);
    private bool ValidPole(Visit visit) => visit.Pole != null && _poles.Contains(visit.Pole) && Owns(visit.Pole) && visit.PolePlacement == visit.Pole.Placement;
    private Item? FreePole()
    {
        if (_poles.Count == 0) {
            return null;
        }

        return _poles.FirstOrDefault(item => Owns(item)
            && !_players.Values.Any(player => player.Tagged && ValidPole(player) && ReferenceEquals(player.Pole, item)));
    }
    private static int Effect(RoomUser actor, bool tagged) => actor.GetClient()?.GetHabbo()?.Gender == "M" ? (tagged ? 45 : 38) : (tagged ? 46 : 39);
    private static int Minutes(Visit visit, DateTimeOffset now) => (int)Math.Clamp((now - visit.EnteredAt).TotalMinutes, 0, int.MaxValue);
    private void Award(GameClient? client, int minutes)
    {
        if (client?.GetHabbo() is { WalletClosed: false } habbo && ReferenceEquals(habbo.CurrentRoom, room) && minutes > 0) {
            achievements.ProgressAchievement(client, "ACH_TagC", minutes);
        }
    }
    private void Apply(RoomUser actor, int effect)
    {
        var habbo = actor.GetClient()?.GetHabbo();

        if (habbo != null && Owns(actor)) {
            actor.CurrentItemEffect = ItemEffectType.Iceskates;
            habbo.Effects?.ApplyEffect(effect);
        }
    }
    private sealed class Visit(Item field, DateTimeOffset enteredAt)
    {
        internal Item Field = field;
        internal long Placement = field.Placement;
        internal long Movement = field.MovementGeneration;
        internal DateTimeOffset EnteredAt = enteredAt;
        internal Item? Pole;
        internal long PolePlacement;
        internal bool Tagged;
    }
}
