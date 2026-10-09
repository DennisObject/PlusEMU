using System.Collections.Immutable;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Sound;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Rooms.Music
{
    public sealed class RoomMusicComponent(IRoomMusicStore store, TimeProvider clock, IAccountSessionGate accounts,
        IGameClientManager clients, IRoomMusicService requests, ILogger<RoomMusicComponent> logger) : IRoomComponent, IDisposable
    {
        private readonly object _sync = new();
        private Room _room = null!;
        private Item? _player;
        private MusicPlayerState _state = new([], 0, null, 0, true);
        private long _armedAt;
        private long _offsetMs;
        private int _lastPosition = -1;
        private int _placement;
        private readonly ConcurrentQueue<InventoryItem> _returned = new();
        private int _publishing;
        private const int PublicationLimit = 4096;
        public int Order => 200;

        public void Initiate(Room room)
        {
            _room = room;
            room.SetMusic(this);
        }
        public void Initiated() { }

        internal bool TryBeginPlacement()
        {
            if (Interlocked.CompareExchange(ref _placement, 1, 0) != 0) {
                return false;
            }

            lock (_sync) {
                if (_player == null || !Attached()) {
                    return true;
                }
            }

            Volatile.Write(ref _placement, 0);

            return false;
        }
        internal void EndPlacement() => Volatile.Write(ref _placement, 0);

        internal void Attach(Item item)
        {
            if (!RoomMusicDefinition.IsPlayer(item.Definition) || item.IsTemporary || _room.MDisposed
                || !ReferenceEquals(item.GetRoom(), _room) || item.RoomId != _room.Id
                || !ReferenceEquals(_room.GetRoomItemHandler().GetItem(item.Id), item)) {
                return;
            }

            lock (_sync) {
                if (_player != null && Attached()) {
                    return;
                }

                var state = store.Load(item.Id);
                _player = item;
                Apply(state.Tracks.Length > RoomMusicDefinition.Capacity(item.Definition) ? state with { Tracks = [], Usable = false } : state);
                item.LegacyDataString = Playing ? "1" : "0";
            }
        }

        internal bool ReturnInvalidPlayer(Item item) => store.ReturnInvalidPlayer(item.Id, _room.Id, item.OwnerId);

        internal void Detach(Item item)
        {
            lock (_sync) {
                if (!ReferenceEquals(_player, item)) {
                    return;
                }

                _player = null;
                _state = new([], 0, null, 0, true);
                _lastPosition = -1;
            }

            _room.SendPacket(new NowPlayingComposer(MusicPlayback.Stopped));
        }

        public void RequestPlaylist(GameClient session, bool machine)
        {
            if (!Admitted(session, out _, out _)) {
                return;
            }

            MusicPlaylist snapshot;

            lock (_sync) {
                snapshot = Playlist();
            }

            session.Send(machine ? new SoundMachinePlaylistComposer(snapshot) : new JukeboxPlaylistComposer(snapshot));
        }

        public void RequestNowPlaying(GameClient session)
        {
            if (!Admitted(session, out _, out _)) {
                return;
            }

            MusicPlayback snapshot;

            lock (_sync) {
                snapshot = Position();
            }

            session.Send(new NowPlayingComposer(snapshot));
        }

        public void Add(GameClient session, uint discId, int position) => Owner(session, () => AddOwned(session, discId, position));
        public void Remove(GameClient session, int position) => Owner(session, () => RemoveOwned(session, position));

        private void Owner(GameClient session, Action action)
        {
            if (!Admitted(session, out _, out var actor)) {
                return;
            }

            if (_room.UsesV2Movement && !RoomOwnerScope.IsOwner(_room)) {
                _room.GetGameMap().Navigation!.RunOwner(actor, (_, _) => action());
            }
            else {
                action();
            }
        }

        private void AddOwned(GameClient session, uint discId, int position)
        {
            // The disc comes out of the loaded inventory, so nothing is queued without one.
            if (!Admitted(session, out var habbo, out var actor) || actor.IsTrading || habbo.Inventory is not { } inventory) {
                return;
            }

            var disc = inventory.Furniture.GetItem(discId);

            if (disc == null || !RoomMusicDefinition.IsDisc(disc.Definition) || !disc.TryReserve()) {
                return;
            }

            bool full = false, changed = false;

            try {
                lock (habbo.WalletSync) {
                    if (!Admitted(session, out var current, out actor) || !ReferenceEquals(current, habbo) || actor.IsTrading
                        || habbo.AccessClosed || !ReferenceEquals(inventory.Furniture.GetItem(discId), disc)) {
                        return;
                    }

                    lock (_sync) {
                        if (!Attached() || _player!.OwnerId != habbo.Id || !_state.Usable || position < 0 || position > _state.Tracks.Length) {
                            return;
                        }

                        var capacity = RoomMusicDefinition.Capacity(_player.Definition);
                        full = _state.Tracks.Length >= capacity;

                        if (!full) {
                            var playing = Position();
                            var startIndex = playing.Position < 0 ? 0 : playing.Position >= position ? playing.Position + 1 : playing.Position;
                            var startedAt = Playing ? clock.GetUtcNow().AddMilliseconds(-playing.ElapsedMs) : (DateTimeOffset?)null;
                            var result = store.Add(_player.Id, _room.Id, habbo.Id, _state.Version, discId, disc.Definition.Id,
                                position, capacity, startIndex, startedAt);

                            if (result == null) {
                                return;
                            }

                            inventory.Furniture.RemoveItem(discId);
                            Apply(result);
                            changed = true;
                        }
                    }
                }
            }
            finally {
                disc.ReleaseReservation();
            }

            if (full) {
                session.Send(new JukeboxPlaylistFullComposer());
            }

            if (!changed) {
                return;
            }

            session.Send(new FurniListRemoveComposer(discId));
            Inventory(session);
            Broadcast();
        }

        private void RemoveOwned(GameClient session, int position)
        {
            if (!Admitted(session, out var habbo, out var actor) || actor.IsTrading) {
                return;
            }

            InventoryItem disc;
            var ownerPass = _room.UsesV2Movement && RoomOwnerScope.IsOwner(_room);

            lock (habbo.WalletSync) {
                if (habbo.AccessClosed || !Admitted(session, out var current, out actor) || !ReferenceEquals(current, habbo) || actor.IsTrading) {
                    return;
                }

                lock (_sync) {
                    if (!Attached() || _player!.OwnerId != habbo.Id || !_state.Usable || position < 0 || position >= _state.Tracks.Length
                        || ownerPass && _returned.Count >= PublicationLimit) {
                        return;
                    }

                    var playing = Position();
                    var nextCount = _state.Tracks.Length - 1;
                    var startIndex = playing.Position < 0 || nextCount == 0 ? 0 : playing.Position == position
                        ? position % nextCount : playing.Position > position ? playing.Position - 1 : playing.Position;
                    var elapsed = playing.Position == position ? 0 : Math.Max(playing.ElapsedMs, 0);
                    var startedAt = Playing && nextCount > 0 ? clock.GetUtcNow().AddMilliseconds(-elapsed) : (DateTimeOffset?)null;
                    var result = store.Remove(_player.Id, _room.Id, habbo.Id, _state.Version, position, startIndex, startedAt);

                    if (result == null) {
                        return;
                    }

                    Apply(result.Value.State);
                    disc = result.Value.Disc;

                    if (ownerPass) {
                        _returned.Enqueue(disc);
                    }
                }
            }

            if (!ownerPass) {
                PublishReturned(disc, accounts, clients, store.AvailableDisc);
            }

            Inventory(session);
            Broadcast();
        }

        internal void PublishPending()
        {
            // Owner callbacks commit under NavigationSync, but account gates must be entered after that monitor is released.
            if (Monitor.IsEntered(_room.NavigationSync) || Interlocked.CompareExchange(ref _publishing, 1, 0) != 0) {
                return;
            }

            try {
                for (var count = 0; count < PublicationLimit && _returned.TryDequeue(out var disc); count++) {
                    try {
                        PublishReturned(disc, accounts, clients, store.AvailableDisc);
                    }
                    catch (Exception error) {
                        logger.LogError(error, "Cannot publish returned song disc {DiscId} to owner {OwnerId}", disc.Id, disc.OwnerId);
                    }
                }
            }
            finally {
                Volatile.Write(ref _publishing, 0);
            }
        }

        internal static void PublishReturned(InventoryItem disc, IAccountSessionGate accounts, IGameClientManager clients,
            Func<uint, uint, InventoryItem?> available)
        {
            if (disc.OwnerId is 0 or > int.MaxValue) {
                return;
            }

            using var account = accounts.Enter((int)disc.OwnerId);
            var client = clients.GetClientByUserId((int)disc.OwnerId);
            var habbo = client?.GetHabbo();

            if (habbo == null) {
                return;
            }

            InventoryItemSnapshot? snapshot = null;

            lock (habbo.WalletSync) {
                if (habbo.AccessClosed || !ReferenceEquals(habbo.Client, client) || habbo.Id != disc.OwnerId) {
                    return;
                }

                var current = available(disc.Id, disc.OwnerId);

                // The disc is already back in storage; an inventory that is not loaded picks it up later.
                if (current != null && habbo.Inventory?.Furniture.AddItem(current) == true) {
                    snapshot = InventoryItemSnapshot.Capture(current);
                }
            }

            if (snapshot != null) {
                client!.Send(new FurniListAddComposer(snapshot));
            }
        }

        public void Inventory(GameClient session) => requests.Inventory(session);

        internal void Use(GameClient session, Item item, int request, bool hasRights)
        {
            if (!hasRights || request < -2 || request == -1) {
                return;
            }

            Owner(session, () => UseOwned(session, item, request));
        }

        private void UseOwned(GameClient session, Item item, int request)
        {
            if (!Admitted(session, out var habbo, out _)) {
                return;
            }

            bool changed;

            lock (_sync) {
                if (!Attached() || !ReferenceEquals(_player, item) || !_state.Usable || _state.Tracks.Length == 0
                    || request >= _state.Tracks.Length || !_room.CheckRights(session, false, true)) {
                    return;
                }

                var startedAt = Playing ? null : (DateTimeOffset?)clock.GetUtcNow();
                var index = request >= 0 ? request : 0;
                var version = store.Playback(item.Id, _room.Id, item.OwnerId, _state.Version, index, startedAt);

                if (version == null) {
                    return;
                }

                Apply(_state with { StartIndex = index, StartedAt = startedAt, Version = version.Value });
                item.LegacyDataString = Playing ? "1" : "0";
                changed = true;
            }

            if (changed) {
                item.UpdateState(false, true);
                Broadcast();
            }
        }

        public void Cycle()
        {
            MusicPlayback? changed = null;

            lock (_sync) {
                if (!Attached() || !Playing) {
                    return;
                }

                var position = Position();

                if (_lastPosition != position.Position) {
                    _lastPosition = position.Position;
                    changed = position;
                }
            }

            if (changed != null) {
                _room.SendPacket(new NowPlayingComposer(changed));
            }
        }

        private void Broadcast()
        {
            MusicPlaylist playlist;
            MusicPlayback playing;
            Item player;
            bool stateChanged;

            lock (_sync) {
                if (!Attached()) {
                    return;
                }

                playlist = Playlist();
                playing = Position();
                _lastPosition = playing.Position;
                player = _player!;
                var state = Playing ? "1" : "0";
                stateChanged = player.LegacyDataString != state;
                player.LegacyDataString = state;
            }

            if (stateChanged) {
                player.UpdateState(false, true);
            }

            _room.SendPacket(new JukeboxPlaylistComposer(playlist));
            _room.SendPacket(new SoundMachinePlaylistComposer(playlist));
            _room.SendPacket(new NowPlayingComposer(playing));
        }

        private bool Attached() => _player is { IsTemporary: false } && !_room.MDisposed
            && ReferenceEquals(_player.GetRoom(), _room) && _player.RoomId == _room.Id
            && ReferenceEquals(_room.GetRoomItemHandler().GetItem(_player.Id), _player);
        private bool Playing => _state.Usable && _state.Tracks.Length > 0 && _state.StartedAt != null;
        private bool Admitted(GameClient session, out Habbo habbo, out RoomUser actor)
        {
            habbo = session.GetHabbo();
            actor = habbo?.CurrentRoom == _room && !_room.MDisposed ? _room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id)! : null!;

            return habbo != null && !habbo.AccessClosed && ReferenceEquals(habbo.Client, session)
                && actor != null && !actor.IsBot && ReferenceEquals(actor.GetClient(), session);
        }

        private void Apply(MusicPlayerState state)
        {
            _state = state;
            _armedAt = clock.GetTimestamp();
            _offsetMs = state.Tracks.Take(state.StartIndex).Sum(track => (long)track.Song.LengthMs);

            if (state.StartedAt is { } start && state.Tracks.Length > 0) {
                var total = state.Tracks.Sum(track => (long)track.Song.LengthMs);
                _offsetMs = (_offsetMs + (long)Math.Max(0, (clock.GetUtcNow() - start).TotalMilliseconds) % total) % total;
            }

            _lastPosition = -1;
        }

        private long Elapsed()
        {
            var total = _state.Tracks.Sum(track => (long)track.Song.LengthMs);

            return total == 0 ? 0 : (_offsetMs + (long)Math.Max(0, clock.GetElapsedTime(_armedAt).TotalMilliseconds) % total) % total;
        }
        private MusicPlayback Position()
        {
            if (!Playing) {
                return MusicPlayback.Stopped;
            }

            var elapsed = Elapsed();

            for (var index = 0; index < _state.Tracks.Length; index++) {
                var track = _state.Tracks[index];

                if (elapsed < track.Song.LengthMs) {
                    var next = (index + 1) % _state.Tracks.Length;

                    return new(track.Song.Id, index, _state.Tracks[next].Song.Id, next, (int)elapsed);
                }

                elapsed -= track.Song.LengthMs;
            }

            return MusicPlayback.Stopped;
        }
        private MusicPlaylist Playlist() => new(_player == null ? 10 : RoomMusicDefinition.Capacity(_player.Definition),
            _state.Tracks, Playing ? (int)Elapsed() : 0);
        public void Dispose()
        {
            lock (_sync) {
                _player = null;
                _state = new([], 0, null, 0, true);
            }
        }
    }
}
