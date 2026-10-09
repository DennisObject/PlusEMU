using Dapper;
using System.Collections.Immutable;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Sound;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Sound;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Music;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests
{
    public partial class PlacedFurniRoomTests
    {
        [Fact]
        public async Task MusicPacketsTransferAtExactSlotAndPublishOnlyAfterCommit()
        {
            var (music, store, _, _) = Music();
            var disc = MusicDisc(31);
            Inventory(disc);
            store.BeforeAdd = () =>
            {
                Assert.Same(disc, Assert.IsType<Plus.HabboHotel.Users.Inventory.InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(31));
                Assert.Empty(_client.Sent);
            };
            await new AddJukeboxDiskEvent().Parse(_room, _client, ClientPacket(31, 0));
            Assert.Equal((31u, 0), Assert.Single(store.Adds));
            Assert.Null(Assert.IsType<Plus.HabboHotel.Users.Inventory.InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(31));
            var body = new FlashIncomingPacket { Buffer = _client.Packets.Last(entry => entry.Header == ServerPacketHeader.JukeboxPlaylistComposer).Body };
            Assert.Equal(10, body.ReadInt());
            Assert.Equal(1, body.ReadInt());
            Assert.Equal(31u, body.ReadUInt());
            Assert.Equal(71, body.ReadInt());
            Assert.False(body.HasDataRemaining());
            Assert.True(disc.TryReserve());
            disc.ReleaseReservation();
            _client.Sent.Clear();
            await new AddJukeboxDiskEvent().Parse(_room, _client, ClientPacket(31, 0));
            Assert.Single(store.Adds);
            Assert.Empty(_client.Sent);
        }

        [Fact]
        public async Task MusicReservedStaleTradingAndFailedWritesKeepInventoryAndPackets()
        {
            var (music, store, _, _) = Music();
            var disc = MusicDisc(31);
            Inventory(disc);
            Assert.True(disc.TryReserve());
            music.Add(_client, 31, 0);
            Assert.Empty(store.Adds);
            disc.ReleaseReservation();
            var actor = _room.GetRoomUserManager().GetRoomUserByHabbo(7);
            actor.IsTrading = true;
            music.Add(_client, 31, 0);
            actor.IsTrading = false;
            Assert.Empty(store.Adds);
            store.Failure = new InvalidOperationException("rollback");
            Assert.Throws<InvalidOperationException>(() => music.Add(_client, 31, 0));
            Assert.Same(disc, Assert.IsType<Plus.HabboHotel.Users.Inventory.InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(31));
            Assert.Empty(_client.Sent);
            Assert.True(disc.TryReserve());
            disc.ReleaseReservation();
            store.Failure = null;
            _client.GetHabbo().Client = null!;
            music.Add(_client, 31, 0);
            Assert.Single(store.Adds);
            Assert.Empty(_client.Sent);
            await Task.CompletedTask;
        }

        [Fact]
        public void MusicPlaybackUsesMonotonicTrackOffsetsAndSkipsLatePollsWithoutBurst()
        {
            var tracks = ImmutableArray.Create(new MusicTrack(31, 7, Song(71, 1000)), new MusicTrack(32, 7, Song(72, 2000)));
            var (music, store, clock, player) = Music(new(tracks, 0, null, 3, true));
            player.Interactor.OnTrigger(_client, player, 0, true);
            Assert.Equal("1", player.LegacyDataString);
            _client.Packets.Clear();
            clock.Advance(1400);
            music.Cycle();
            var body = new FlashIncomingPacket { Buffer = Assert.Single(_client.Packets).Body };
            Assert.Equal(new[] { 72, 1, 71, 0, 400 }, Enumerable.Range(0, 5).Select(_ => body.ReadInt()));
            _client.Packets.Clear();
            clock.Utc = clock.Utc.AddDays(20);
            clock.Advance(9000, changeUtc: false);
            music.Cycle();
            Assert.Empty(_client.Packets); // Same track after three whole loops; no burst or UTC-driven seek.
            music.RequestNowPlaying(_client);
            body = new FlashIncomingPacket { Buffer = Assert.Single(_client.Packets).Body };
            Assert.Equal(new[] { 72, 1, 71, 0, 400 }, Enumerable.Range(0, 5).Select(_ => body.ReadInt()));
            player.Interactor.OnTrigger(_client, player, 1, true);
            Assert.Equal("0", player.LegacyDataString);
            Assert.Null(store.State.StartedAt);
        }

        [Fact]
        public void MusicPickupFailureLeavesPlaybackAttachedAndSuccessStopsBeforeRemoval()
        {
            var (music, store, _, player) = Music(new([new(31, 7, Song(71, 1000))], 0, DateTimeOffset.Parse("2040-02-03T04:05:06Z"), 1, true));
            var failed = new RoomItemPickupService(TestGameClientManager.Empty, TestItemRuntime.Quests, new PickupStore(_ => false));
            _client.Sent.Clear();
            failed.PickUp(_client, player.Id).GetAwaiter().GetResult();
            Assert.Same(player, _room.GetRoomItemHandler().GetItem(player.Id));
            Assert.Empty(_client.Sent);
            var success = new RoomItemPickupService(TestGameClientManager.Empty, TestItemRuntime.Quests, new PickupStore(request =>
            {
                Assert.True(request.MusicPlayer);
                Assert.Same(player, _room.GetRoomItemHandler().GetItem(player.Id));
                store.State = store.State with { StartedAt = null, Version = 2 };

                return true;
            }));
            success.PickUp(_client, player.Id).GetAwaiter().GetResult();
            Assert.Null(_room.GetRoomItemHandler().GetItem(player.Id));
            Assert.Null(player.GetRoom());
            Assert.Equal("0", Assert.IsType<Plus.HabboHotel.Users.Inventory.InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(player.Id)!.ExtraData.Serialize());
            _client.Packets.Clear();
            music.RequestNowPlaying(_client);
            var body = new FlashIncomingPacket { Buffer = Assert.Single(_client.Packets).Body };
            Assert.Equal(Enumerable.Repeat(-1, 5), Enumerable.Range(0, 5).Select(_ => body.ReadInt()));
        }

        [Fact]
        public void MusicComposersCarryNativeIdentityCapacityAndMillisecondFields()
        {
            var songs = ImmutableArray.Create(Song(71, 1300), Song(72, 2700));
            var tracks = ImmutableArray.Create(new MusicTrack(31, 7, songs[0]), new MusicTrack(32, 8, songs[1]));
            var packet = new HabbiconTestSupport.RecordingPacket();
            new SoundMachinePlaylistComposer(new(20, tracks, 1800)).Compose(packet);
            Assert.Equal(new object[] { 1800, 2, 71, 1300, "song71", "author", 72, 2700, "song72", "author" }, packet.Writes);
            packet = new();
            new TraxSongInfoComposer(songs).Compose(packet);
            Assert.Equal(new object[] { 2, 71, "code71", "song71", "1,1,1", 1300, "author", 72, "code72", "song72", "1,1,1", 2700, "author" }, packet.Writes);
            packet = new();
            new OfficialSongIdComposer("code71", 71).Compose(packet);
            Assert.Equal(new object[] { "code71", 71 }, packet.Writes);
        }

        [Fact]
        public void MusicBulkPickupFailureCannotFallThroughToOrdinaryRemoval()
        {
            var (_, _, _, player) = Music();
            var ordinary = Add(40, 2, 2);
            var pickup = new RoomItemPickupService(TestGameClientManager.Empty, TestItemRuntime.Quests, new PickupStore(_ => false));
            new Plus.HabboHotel.Rooms.Chat.Commands.User.PickAllCommand(_database, pickup).Execute(_client, _room, []);
            Assert.Same(player, _room.GetRoomItemHandler().GetItem(30));
            Assert.Same(ordinary, _room.GetRoomItemHandler().GetItem(40));
            Assert.Empty(_client.Sent);
            player.OwnerId = 8;
            player.UserId = 8;
            new Plus.HabboHotel.Rooms.Chat.Commands.User.EjectAllCommand(TestGameClientManager.Empty, _database, pickup).Execute(_client, _room, []);
            Assert.Same(player, _room.GetRoomItemHandler().GetItem(30));
            Assert.Empty(_client.Sent);
        }

        [Fact]
        public void MusicQueuedMutationRejectsDepartedActorsAndDetachedPlayerInstances()
        {
            var (music, store, _, player) = Music(v2: true);
            Inventory(MusicDisc(31));
            music.Add(_client, 31, 0);
            Assert.Empty(store.Adds);
            _room.GetRoomUserManager().RemoveUserFromRoom(_client, false, false);
            ExecutorTick();
            Assert.Empty(store.Adds);
            Assert.NotNull(Assert.IsType<Plus.HabboHotel.Users.Inventory.InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(31));
            Assert.DoesNotContain(ServerPacketHeader.FurniListRemoveComposer, _client.Sent);
            Assert.Same(player, _room.GetRoomItemHandler().GetItem(player.Id));
        }

        [Fact]
        public void MusicSelectedTrackControllerToggleAndInvalidRequestsMatchNativeEditor()
        {
            var (music, store, _, player) = Music(new([new(31, 7, Song(71, 1000)), new(32, 7, Song(72, 2000))], 0, null, 0, true));
            player.Interactor.OnTrigger(_client, player, -1, true);
            player.Interactor.OnTrigger(_client, player, 2, true);
            player.Interactor.OnTrigger(_client, player, 0, false);
            Assert.Null(store.State.StartedAt);
            Assert.Empty(_client.Sent);
            player.Interactor.OnTrigger(_client, player, 1, true);
            Assert.Equal(1, store.State.StartIndex);
            var body = new FlashIncomingPacket { Buffer = _client.Packets.Last(entry => entry.Header == ServerPacketHeader.NowPlayingComposer).Body };
            Assert.Equal(new[] { 72, 1, 71, 0, 0 }, Enumerable.Range(0, 5).Select(_ => body.ReadInt()));
            player.Interactor.OnTrigger(_client, player, -2, true);
            Assert.Null(store.State.StartedAt);
        }

        [RoomMusicDatabaseFact]
        public async Task MusicRoomInitiationAndReloadAttachPersistedPlaylistWithoutOnPlace()
        {
            await using var fixture = await RoomMusicDatabaseTests.Fixture.Create();
            Viewer();
            _client.GetHabbo().Client = _client;
            var store = fixture.Store;
            var state = store.Add(30, 42, 7, 0, 31, 100, 0, 10, 0, null)!;
            var clock = new MusicClock();
            Assert.NotNull(store.Playback(30, 42, 7, state.Version, 0, clock.Utc.AddMilliseconds(-300)));
            var music = new RoomMusicComponent(store, clock, new AccountSessionGate(), TestGameClientManager.Empty, new RoomMusicService(store), TestLogging.For<RoomMusicComponent>());
            var furniture = new RoomFurnitureLoader(fixture.Database, fixture.Definitions);
            Set("_components", new IRoomComponent[] { new RoomDataComponent(furniture), music });
            _room.Initiate();
            var first = _room.GetRoomItemHandler().GetItem(30)!;
            Assert.Equal("1", first.LegacyDataString);
            _client.Packets.Clear();
            music.RequestNowPlaying(_client);
            var body = new FlashIncomingPacket { Buffer = Assert.Single(_client.Packets).Body };
            Assert.Equal(new[] { 71, 0, 71, 0, 300 }, Enumerable.Range(0, 5).Select(_ => body.ReadInt()));
            _room.GetRoomItemHandler().LoadFurniture(furniture.Load(42));
            var replacement = _room.GetRoomItemHandler().GetItem(30)!;
            Assert.NotSame(first, replacement);
            Assert.Null(first.GetRoom());
            replacement.Interactor.OnTrigger(_client, replacement, 0, true);
            Assert.Null(store.Load(30).StartedAt);
            Assert.Equal("0", replacement.LegacyDataString);
        }

        [RoomMusicDatabaseFact]
        public async Task MusicReturnedDiscPublicationRevalidatesDeletionAndForeignTransferBeforeOwnerGate()
        {
            foreach (var foreign in new[] { false, true }) {
                await using var fixture = await RoomMusicDatabaseTests.Fixture.Create();
                var id = foreign ? 33u : 31u;
                var owner = foreign ? 8u : 7u;
                var captured = fixture.Store.AvailableDisc(id, owner)!;
                var habbo = new Plus.HabboHotel.Users.Habbo { Id = (int)owner, Inventory = new Plus.HabboHotel.Users.Inventory.InventoryComponent { Furniture = new([], []) } };
                var (client, sent) = HabbiconTestSupport.Client(habbo);
                habbo.Client = client;
                var clients = Proxy<Plus.HabboHotel.GameClients.IGameClientManager>((name, args) => name == "GetClientByUserId" && (int)args[0]! == owner ? client : null);
                var gate = new AccountSessionGate();
                var held = gate.Enter((int)owner);
                Task? pending = null;
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                try {
                    pending = Task.Run(() => { started.SetResult(); RoomMusicComponent.PublishReturned(captured, gate, clients, fixture.Store.AvailableDisc); });
                    await started.Task;
                    await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromMilliseconds(100)));

                    if (foreign) {
                        await fixture.Connection.ExecuteAsync("UPDATE items SET user_id=7 WHERE id=33");
                    }
                    else {
                        new InventoryClearStore(fixture.Database, fixture.Definitions).DeleteAll(7);
                    }

                    Assert.Empty(habbo.Inventory.Furniture.GetItems);
                }
                finally {
                    held.Dispose();

                    if (pending != null) {
                        await pending.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                }

                Assert.Null(habbo.Inventory.Furniture.GetItem(id));
                Assert.Empty(sent);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MusicInventoryQueryCannotPublishAcrossAReplacedOrDetachedHabbo(bool replace)
        {
            var (_, store, _, _) = Music();
            Inventory(MusicDisc(31));
            _client.GetHabbo().CurrentRoom = null; // Native disc inventory is available in hotel view too.
            store.BeforeMetadata = () =>
            {
                typeof(Plus.HabboHotel.GameClients.GameClient).GetField("_habbo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(_client, replace ? new Plus.HabboHotel.Users.Habbo { Id = 8, Client = _client } : null);
            };
            await new GetUserSongDisksEvent(new RoomMusicService(store)).Parse(_client, ClientPacket());
            Assert.Empty(_client.Sent);
        }

        [RoomMusicDatabaseFact]
        public async Task MusicClearPublishesReleasedOwnAndForeignDiscsToTheirCurrentSessions()
        {
            await using var fixture = await RoomMusicDatabaseTests.Fixture.Create();
            Viewer();
            _client.GetHabbo().Client = _client;
            var state = fixture.Store.Add(30, 42, 7, 0, 31, 100, 0, 10, 0, null)!;
            await fixture.Connection.ExecuteAsync("UPDATE items SET user_id=8 WHERE id=30");
            fixture.Store.Add(30, 42, 8, state.Version, 33, 100, 1, 10, 0, null);
            await fixture.Connection.ExecuteAsync("UPDATE items SET user_id=7 WHERE id=30");
            Assert.True(new RoomItemPickupStore(fixture.Database).PickUp(new(30, 42, 7, 7, InteractionType.Jukebox, true)));
            var loaded = await new FurnitureInventoryLoader(fixture.Database, fixture.Definitions).Load(7);
            _client.GetHabbo().Inventory = new() { Furniture = new(loaded, []) };
            var foreign = new Plus.HabboHotel.Users.Habbo { Id = 8, Inventory = new Plus.HabboHotel.Users.Inventory.InventoryComponent { Furniture = new([], []) } };
            var (foreignClient, sent) = HabbiconTestSupport.Client(foreign);
            foreign.Client = foreignClient;
            var clients = Proxy<Plus.HabboHotel.GameClients.IGameClientManager>((name, args) => name == "GetClientByUserId" ? (int)args[0]! == 7 ? _client : foreignClient : null);
            var clear = new InventoryClearService(new InventoryClearStore(fixture.Database, fixture.Definitions), new AccountSessionGate(), clients);
            Assert.True(clear.TryClear(_client, _room));
            Assert.Equal(31u, Assert.Single(Assert.IsType<Plus.HabboHotel.Users.Inventory.InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItems).Id);
            Assert.Equal(33u, Assert.Single(foreign.Inventory.Furniture.GetItems).Id);
            Assert.Equal(ServerPacketHeader.FurniListAddComposer, Assert.Single(sent).Header);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MusicReturnedDiscWaitsForAccountWithoutBlockingNavigationOrTeardown(bool nested)
        {
            var gate = new AccountSessionGate();
            var (music, store, _, _) = Music(new([new(31, 7, Song(71, 1000))], 0, null, 0, true), v2: true, accounts: gate);
            Inventory(MusicDisc(40));
            var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            store.BeforeRemove = () => removed.TrySetResult();
            var held = gate.Enter(7);
            Task? tick = null;
            Task? teardown = null;

            try {
                if (nested) {
                    tick = Task.Run(() => _room.RunFastPass(() => _room.RunFastPass(() => music.Remove(_client, 0))));
                }
                else {
                    music.Remove(_client, 0);
                    tick = Task.Run(ExecutorTick);
                }

                await removed.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await Assert.ThrowsAsync<TimeoutException>(() => tick.WaitAsync(TimeSpan.FromMilliseconds(100)));
                teardown = Task.Run(() =>
                {
                    // Use real V2 room teardown while the committed return waits outside its navigation monitor.
                    _room.MutedUsers = new();
                    _room.UsersWithRights = new();
                    _room.WordFilterList = new();
                    Set("_tents", new Dictionary<uint, List<Plus.HabboHotel.Rooms.RoomUser>>());
                    _room.Dispose();
                    music.Dispose();
                });
                await teardown.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.True(_room.MDisposed);
                Assert.Null(Assert.IsType<Plus.HabboHotel.Users.Inventory.InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(31));
            }
            finally {
                held.Dispose();

                if (tick != null) {
                    await tick.WaitAsync(TimeSpan.FromSeconds(5));
                }

                if (teardown != null) {
                    await teardown.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }

            Assert.NotNull(Assert.IsType<Plus.HabboHotel.Users.Inventory.InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(31));
        }

        [Theory]
        [InlineData("NITRO-1-6-6")]
        [InlineData("NITRO-3-6-0")]
        [InlineData("OCTANE-3-6-0-FLOOR-20260909")]
        public async Task MusicSupportedProfilesResolveNativeRequestsAndPublishDiscInventory(string name)
        {
            var (_, store, _, _) = Music();
            Inventory(MusicDisc(31));
            var directory = Directory.CreateTempSubdirectory("music-revisions-").FullName;

            try {
                foreach (var file in Directory.GetFiles(Path.Join(AppContext.BaseDirectory, "revisions"), "*.json")) {
                    File.Copy(file, Path.Join(directory, Path.GetFileName(file)));
                }

                var cache = new Plus.Communication.Revisions.RevisionsCache();
                typeof(Plus.Communication.Revisions.RevisionsCache).GetField("_directory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(cache, directory);
                await cache.Start();
                var profile = cache.Revisions[name];
                var requests = new (uint Header, uint Internal)[] {
                    (753, Plus.Communication.Packets.Incoming.ClientPacketHeader.AddJukeboxDiskEvent),
                    (3050, Plus.Communication.Packets.Incoming.ClientPacketHeader.RemoveJukeboxDiskEvent),
                    (1435, Plus.Communication.Packets.Incoming.ClientPacketHeader.GetJukeboxPlaylistEvent),
                    (3498, Plus.Communication.Packets.Incoming.ClientPacketHeader.GetSoundMachinePlaylistEvent),
                    (3082, Plus.Communication.Packets.Incoming.ClientPacketHeader.GetSongInfoEvent),
                    (2304, Plus.Communication.Packets.Incoming.ClientPacketHeader.GetUserSongDisksEvent),
                    (1325, Plus.Communication.Packets.Incoming.ClientPacketHeader.GetNowPlayingEvent),
                    (3189, Plus.Communication.Packets.Incoming.ClientPacketHeader.GetOfficialSongIdEvent)
                };

                foreach (var request in requests) {
                    Assert.Equal(request.Internal, profile.IncomingIdToInternalIdMapping[request.Header]);
                }

                var responses = new (uint Internal, uint Header)[] {
                    (ServerPacketHeader.JukeboxPlaylistComposer, 34), (ServerPacketHeader.JukeboxPlaylistFullComposer, 105),
                    (ServerPacketHeader.NowPlayingComposer, 469), (ServerPacketHeader.OfficialSongIdComposer, 1381),
                    (ServerPacketHeader.SoundMachinePlaylistComposer, 1748), (ServerPacketHeader.TraxSongInfoComposer, 3365),
                    (ServerPacketHeader.SongDisksInventoryComposer, 2602)
                };

                foreach (var response in responses) {
                    Assert.Equal(response.Header, profile.InternalIdToOutgoingIdMapping[response.Internal]);
                }

                _client.Revision = profile;
                await new GetUserSongDisksEvent(new RoomMusicService(store)).Parse(_client, ClientPacket());
                var packet = Assert.Single(_client.Packets);
                Assert.Equal(2602u, packet.Header);
                var body = new FlashIncomingPacket { Buffer = packet.Body };
                Assert.Equal(new[] { 1, 31, 71 }, Enumerable.Range(0, 3).Select(_ => body.ReadInt()));
            }
            finally {
                Directory.Delete(directory, recursive: true);
            }
        }

        private (RoomMusicComponent Music, MusicStore Store, MusicClock Clock, Item Player) Music(MusicPlayerState? state = null, bool v2 = false, AccountSessionGate? accounts = null)
        {
            if (v2) {
                _room.EnableV2Movement();
                ExecutorActor(3, 3);
            }
            else {
                Viewer();
            }

            _client.GetHabbo().Client = _client;
            var clock = new MusicClock();
            var store = new MusicStore { State = state ?? new([], 0, null, 0, true) };
            var music = new RoomMusicComponent(store, clock, accounts ?? new AccountSessionGate(), Proxy<Plus.HabboHotel.GameClients.IGameClientManager>((name, args) => name == "GetClientByUserId" && (int)args[0]! == 7 ? _client : null), new RoomMusicService(store), TestLogging.For<RoomMusicComponent>());
            music.Initiate(_room);
            var player = Furni(30, InteractionType.Jukebox, WiredBoxType.None);
            player.Definition.ItemName = "jukebox*1";
            player.UserId = 7;
            player.ExtraData = Plus.HabboHotel.Items.FurniExtraData.Load(player.Definition, "0", keepLegacy: true);
            Assert.True(_room.GetRoomItemHandler().SetFloorItem(_client, player, 1, 1, 0, true, false, false));
            _client.Sent.Clear();
            _client.Packets.Clear();

            return (music, store, clock, player);
        }
        private static InventoryItem MusicDisc(uint id) => new() { Id = id, OwnerId = 7, Definition = new() { Id = 100, InteractionType = InteractionType.MusicDisc, Type = ItemType.Floor } };
        private static MusicSong Song(int id, int length) => new(id, "code" + id, "song" + id, "author", "1,1,1", length);
        private sealed class MusicClock : TimeProvider
        {
            private long _timestamp;
            public DateTimeOffset Utc = DateTimeOffset.Parse("2040-02-03T04:05:06Z");
            public override DateTimeOffset GetUtcNow() => Utc;
            public override long GetTimestamp() => _timestamp;
            public override long TimestampFrequency => 1000;
            public void Advance(long milliseconds, bool changeUtc = true)
            {
                _timestamp += milliseconds;

                if (changeUtc) {
                    Utc = Utc.AddMilliseconds(milliseconds);
                }
            }
        }
        private sealed class MusicStore : IRoomMusicStore
        {
            public MusicPlayerState State = new([], 0, null, 0, true);
            public List<(uint DiscId, int Position)> Adds = [];
            public Action? BeforeAdd;
            public Action? BeforeMetadata;
            public Action? BeforeRemove;
            public Exception? Failure;
            public IReadOnlyList<MusicSong> Songs(IReadOnlyList<int> ids) => ids.Select(id => Song(id, 1000)).ToArray();
            public int OfficialSong(string code) => 71;
            public IReadOnlyList<MusicTrack> Inventory(int ownerId, IReadOnlyList<uint> ids)
            {
                BeforeMetadata?.Invoke();

                return ids.Select(id => new MusicTrack(id, (uint)ownerId, Song(71, 1000))).ToArray();
            }
            public MusicPlayerState Load(uint playerId) => State;
            public InventoryItem? AvailableDisc(uint discId, uint ownerId) => MusicDisc(discId);
            public bool ReturnInvalidPlayer(uint playerId, uint roomId, uint ownerId) => true;
            public MusicPlayerState? Add(uint playerId, uint roomId, int ownerId, long version, uint discId, uint baseItem, int position, int capacity, int startIndex, DateTimeOffset? startedAt)
            {
                Adds.Add((discId, position));
                BeforeAdd?.Invoke();

                if (Failure != null) {
                    throw Failure;
                }

                return State = State with { Tracks = State.Tracks.Insert(position, new(discId, (uint)ownerId, Song(71, 1000))), StartIndex = startIndex, StartedAt = startedAt, Version = version + 1 };
            }
            public (MusicPlayerState State, InventoryItem Disc)? Remove(uint playerId, uint roomId, int ownerId, long version, int position, int startIndex, DateTimeOffset? startedAt)
            {
                BeforeRemove?.Invoke();
                var track = State.Tracks[position];
                State = State with { Tracks = State.Tracks.RemoveAt(position), StartIndex = startIndex, StartedAt = startedAt, Version = version + 1 };
                var disc = MusicDisc(track.DiscId);
                disc.OwnerId = track.OwnerId;

                return (State, disc);
            }
            public long? Playback(uint playerId, uint roomId, uint ownerId, long version, int startIndex, DateTimeOffset? startedAt)
            {
                State = State with { StartIndex = startIndex, StartedAt = startedAt, Version = version + 1 };

                return State.Version;
            }
        }
    }
}
