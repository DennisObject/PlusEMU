using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Collections.Concurrent;
using System.Buffers.Binary;
using Plus.Communication.Flash;
using Plus.Communication.Revisions;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Users;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Xunit;
using Xunit.Abstractions;

namespace Plus.Tests;

public sealed class WiredVariableDatabaseFactAttribute : FactAttribute
{
    public WiredVariableDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WIRED_VARIABLE_PREVIEW_CONFIG") is null)
            Skip = "Opt-in isolated plus-wired-preview database probe.";
    }
}

public sealed class WiredVariableDatabaseTests(ITestOutputHelper output)
{
    [WiredVariableDatabaseFact]
    public async Task RealMySqlPersistenceAuthorizationConcurrencyAndBatchedReads()
    {
        var connectionString = GuardedConnectionString();
        using var admin = new MySqlConnection(connectionString); await admin.OpenAsync();
        Assert.Equal(2, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN ('wired_variable_locks','wired_variable_values')"));
        var users = new List<uint>(); var rooms = new List<uint>(); var items = new List<uint>();
        try
        {
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var owner = Insert(admin, "users", new() { ["username"] = "wv_" + suffix, ["password"] = Guid.NewGuid().ToString("N"), ["mail"] = suffix + "@invalid" }); users.Add(owner);
            var room = Insert(admin, "rooms", new() { ["owner"] = owner.ToString(), ["caption"] = "Disposable wired variable probe", ["model_name"] = admin.QueryFirst<string>("SELECT id FROM room_models LIMIT 1") }); rooms.Add(room);
            var database = new ProbeDatabase(connectionString);
            var directory = new DatabaseWiredVariableDirectory(database);
            var store = new DatabaseWiredVariableStore(database);
            var baseItem = admin.QueryFirst<uint>("SELECT id FROM furniture LIMIT 1");
            for (var i = 0; i < 6; i++)
            {
                var item = Insert(admin, "items", new() { ["user_id"] = owner, ["room_id"] = room, ["base_item"] = baseItem, ["extra_data"] = "", ["wall_pos"] = "" }); items.Add(item);
                admin.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES (@item,'wf_var_user',1,@configuration)",
                    new { item, configuration = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 10], Text = "probe" + i }) });
            }
            Assert.Equal(owner, directory.GetRoomOwner(room)); Assert.Equal(owner, directory.Find(items[0])!.OwnerId);
            var holders = Enumerable.Range(1, 200).Select(i =>
            {
                var id = Insert(admin, "users", new() { ["username"] = $"wv_{suffix}_{i}", ["password"] = Guid.NewGuid().ToString("N"), ["mail"] = $"{suffix}_{i}@invalid" });
                users.Add(id); return new WiredVariableHolder(WiredVariableTarget.User, id, i);
            }).ToArray();
            var frame = new WiredVariableFrame(room, holders);
            var module = new WiredVariableModule(room, directory, store, () => 1000);
            var reference = new WiredVariableReference(WiredVariableTarget.User, $"custom:{items[0]}");
            Assert.True(module.Mutate(reference, holders[0], WiredVariableMutation.Give, 1, frame));
            var reconnectedHolder = holders[0] with { EntityId = 999 };
            var reloaded = new WiredVariableModule(room, new DatabaseWiredVariableDirectory(database), new DatabaseWiredVariableStore(database), () => 2000);
            Assert.Equal(1, reloaded.Read(reference, reconnectedHolder, new(room, [reconnectedHolder]))!.Value);
            var modules = Enumerable.Range(0, 4).Select(_ => new WiredVariableModule(room, directory, store, () => 3000)).ToArray();
            await Task.WhenAll(modules.Select(m => Task.Run(() =>
            {
                for (var n = 0; n < 10; n++) Assert.True(m.Change(reference, holders[0], WiredVariableMutation.Set, value => value + 1, frame));
            })));
            Assert.Equal(41, module.Read(reference, holders[0], frame)!.Value);
            await Task.WhenAll(modules.Select(m => Task.Run(() => m.Mutate(reference, holders[1], WiredVariableMutation.Give, 5, frame))));
            Assert.Equal(1, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_variable_values WHERE definition_id=@id AND holder_id=@holder", new { id = items[0], holder = holders[1].StableId }));

            // Change the authoritative database after normal resolution, before the actual store transaction.
            var transactionDb = new ProbeDatabase(connectionString);
            var race = new WiredVariableModule(room, directory, new DatabaseWiredVariableStore(transactionDb), () => 4000);
            foreach (var change in new[] { "owner", "configuration", "placement" })
            {
                transactionDb.BeforeConnection = () =>
                {
                    if (change == "owner") admin.Execute("UPDATE rooms SET owner='malformed-owner' WHERE id=@room", new { room });
                    else if (change == "configuration") admin.Execute("UPDATE wired_item_configurations SET configuration=@config WHERE item_id=@id", new { id = items[0], config = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 10], Text = "changed" }) });
                    else admin.Execute("UPDATE items SET room_id=0 WHERE id=@id", new { id = items[0] });
                };
                Assert.False(race.Mutate(reference, holders[0], WiredVariableMutation.Set, 999, frame)); Assert.Empty(race.DrainChanges());
                Assert.Equal(41, store.Read(new(items[0], WiredVariableTarget.User, holders[0].StableId))!.Value);
                admin.Execute("UPDATE rooms SET owner=@owner WHERE id=@room", new { owner = owner.ToString(), room });
                admin.Execute("UPDATE items SET room_id=@room WHERE id=@id", new { room, id = items[0] });
                admin.Execute("UPDATE wired_item_configurations SET configuration=@config WHERE item_id=@id", new { id = items[0], config = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 10], Text = "probe0" }) });
            }
            admin.Execute("UPDATE rooms SET owner=@owner WHERE id=@room", new { owner = owner + "junk", room });
            Assert.Null(directory.GetRoomOwner(room)); Assert.Null(directory.Find(items[0]));
            admin.Execute("UPDATE rooms SET owner=@owner WHERE id=@room", new { owner = owner.ToString(), room });

            foreach (var item in items)
                foreach (var holder in holders)
                    admin.Execute("INSERT INTO wired_variable_values(definition_id,target_kind,holder_id,value,created_at_ms,updated_at_ms) VALUES (@item,0,@holder,25,1000,1000) ON DUPLICATE KEY UPDATE value=25", new { item, holder = holder.StableId });
            var references = items.Select(item => new WiredVariableReference(WiredVariableTarget.User, $"custom:{item}")).ToArray();
            database.Commands = 0;
            using (var snapshot = module.CaptureReads(references, frame))
                foreach (var variable in references)
                    foreach (var holder in holders) Assert.Equal(25, snapshot.Read(variable, holder, frame)!.Value);
            Assert.Equal(13, database.Commands);
            output.WriteLine("Actual MySQL 200 holders × 6 variables: 13 commands; 1200 populated values.");

            database.Commands = 0;
            var holderPage = store.ReadPage(items[0], WiredVariableTarget.User, 2, 15, -1);
            Assert.Equal(200, holderPage.Total); Assert.Equal(15, holderPage.Holders.Count);
            Assert.Equal(holders[15].StableId, holderPage.Holders[0].Key.HolderId);
            Assert.StartsWith("wv_", holderPage.Holders[0].Name); Assert.Equal(2, database.Commands);
            var filteredPage = module.ReadHolderPage(items[0], 1, 200, 2, holders.Take(3).Select(x => x.StableId).ToArray());
            Assert.Equal(3, filteredPage.Total); Assert.Equal(3, filteredPage.Holders.Count);
            Assert.Empty(store.ReadPage(items[0], WiredVariableTarget.User, int.MaxValue, 200, 0).Holders);
            output.WriteLine("Actual MySQL bounded holder page: 2 commands, 15 of 200 rows; filtered and overflow pages passed.");

            var globalItem = Insert(admin, "items", new() { ["user_id"] = owner, ["room_id"] = room, ["base_item"] = baseItem, ["extra_data"] = "", ["wall_pos"] = "" }); items.Add(globalItem);
            var liveRoom = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)); liveRoom.Id = room; liveRoom.OwnerId = (int)owner;
            var atomicDb = new ProbeDatabase(connectionString);
            var roomVariables = new WiredRoomVariables(liveRoom, atomicDb, () => 5000);
            var itemHandler = new RoomItemHandling(liveRoom, TestRoomItemStore.Instance);
            typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(liveRoom, itemHandler);
            var roomUsers = new RoomUserManager(liveRoom, TestRoomUserStore.Instance, TimeProvider.System);
            typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(liveRoom, roomUsers);
            var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(itemHandler)!;
            var userDefinitionItem = new Item { Id = items[0], OwnerId = owner, Definition = new() { InteractionName = "wf_var_user" } };
            floor[userDefinitionItem.Id] = userDefinitionItem;
            var userDefinition = roomVariables.CreateBox(userDefinitionItem)!;
            userDefinition.ApplyConfiguration(new() { IntParams = [1, 10], Text = "probe0" }); roomVariables.ConfigurationLoaded(userDefinition);
            var fxItemId = Insert(admin, "items", new() { ["user_id"] = owner, ["room_id"] = room, ["base_item"] = baseItem, ["extra_data"] = "", ["wall_pos"] = "" }); items.Add(fxItemId);
            var fxItem = new Item { Id = fxItemId, OwnerId = owner, Definition = new() { InteractionName = "wf_xtra_var_fx_health" } }; floor[fxItem.Id] = fxItem;
            var fxBox = Assert.IsType<WiredVariableMetadataBox>(roomVariables.CreateBox(fxItem)); roomVariables.ConfigurationLoaded(fxBox);
            var sentFx = new List<uint>();
            var fxClient = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
            {
                Revision = new Revision { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>
                {
                    [9473] = 9473, [9474] = 9474, [9475] = 9475, [9476] = 9476
                } },
                SendCallback = args => { sentFx.Add(BinaryPrimitives.ReadUInt16BigEndian(args.MemoryBuffer.Span.Slice(4, 2))); return true; }
            };
            var fxPlayer = new Habbo { Id = (int)holders[0].StableId, Client = fxClient, CurrentRoom = liveRoom }; fxClient.SetHabbo(fxPlayer);
            var fxUser = new RoomUser(fxPlayer.Id, 0, holders[0].EntityId, liveRoom);
            typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fxUser, fxClient);
            var liveUsers = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(roomUsers)!;
            liveUsers[fxUser.VirtualId] = fxUser;
            var signFrame = new WiredVariableFrame(room, [WiredVariableRuntimeFrames.UserHolder(fxUser)]);
            var signReference = new WiredVariableReference(WiredVariableTarget.User, "internal:@sign");
            Assert.Equal(-1, roomVariables.Module.Read(signReference, signFrame.Holders[0], signFrame)!.Value);
            fxUser.SetStatus("sign", "7"); Assert.Equal(7, roomVariables.Module.Read(signReference, signFrame.Holders[0], signFrame)!.Value);
            fxUser.RemoveStatus("sign"); Assert.Equal(-1, roomVariables.Module.Read(signReference, signFrame.Holders[0], signFrame)!.Value);
            var legacySnapshot = new WiredVariableMenu(liveRoom, roomVariables).Snapshot();
            Assert.Equal(holders[0].StableId, Assert.Single(legacySnapshot.Assignments).Key.HolderId);
            Assert.Single(legacySnapshot.Definitions);
            var fxFrame = new WiredVariableFrame(room, [holders[0]]);
            var viewers = new[] { new WiredVariableFxViewer(fxUser, [holders[0]]) };
            Assert.True(roomVariables.FlushFx(fxFrame, viewers, (client, packet) => client.Send(packet), exception => throw exception));
            Assert.Equal(new uint[] { 9473, 9475 }, sentFx); sentFx.Clear(); atomicDb.Commands = 0;
            Assert.False(roomVariables.FlushFx(fxFrame, viewers, (client, packet) => client.Send(packet), exception => throw exception));
            Assert.Equal(0, atomicDb.Commands); Assert.Empty(sentFx);
            roomVariables.InvalidateFx();
            var failedSend = false;
            roomVariables.Fx.RemoveViewer(fxPlayer.Id);
            roomVariables.FlushFx(fxFrame, viewers, (_, _) => throw new IOException("Injected enqueue failure"), _ => failedSend = true);
            Assert.True(failedSend); Assert.True(roomVariables.FxDirty);
            roomVariables.FlushFx(fxFrame, viewers, (client, packet) => client.Send(packet), exception => throw exception);
            Assert.Equal(new uint[] { 9473, 9475 }, sentFx); sentFx.Clear();
            fxItem.SetState(1, 1, 0, []);
            roomVariables.FlushFx(fxFrame, viewers, (client, packet) => client.Send(packet), exception => throw exception);
            Assert.Equal(new uint[] { 9476 }, sentFx);
            output.WriteLine("Actual room FX binding/composition: initial configs/status, unchanged flush zero SQL, enqueue-failure retry, and moved-off-variable removal passed.");
            // Exercise production readiness and cycle entry with the same actual SQL module, not FlushFx directly.
            roomVariables.Fx.RemoveViewer(fxPlayer.Id); // End the preceding module-only simulated viewer session.
            var nativeWired = new WiredComponent(liveRoom, TestLogging.Logger, TimeProvider.System, TestWiredRoomSettingsFactory.Instance);
            typeof(Room).GetField("_wiredComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(liveRoom, nativeWired);
            typeof(WiredComponent).GetField("_variables", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(nativeWired, new Lazy<WiredRoomVariables>(() => roomVariables));
            Assert.Same(roomVariables, nativeWired.Variables);
            typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(liveRoom,
                new Gamemap(liveRoom, new RoomModel("wired-sql-probe", 0, 0, 0, 0, "000\r000\r000", 0, 0, true), TestLogging.Navigation));
            fxPlayer.Username = "probe-viewer"; fxPlayer.Motto = ""; fxPlayer.Look = "test"; fxPlayer.Gender = "M";
            fxPlayer.HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
            fxPlayer.Effects = new(new FixedTimeProvider(FixedTimeProvider.Epoch)); fxPlayer.Access = EditorTestSupport.Access([]);
            fxClient.Revision.InternalIdToOutgoingIdMapping = typeof(ServerPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(uint)).Select(field => (uint)field.GetValue(null)!).Distinct().ToDictionary(id => id, id => id);
            var failSnapshot = false; var failFx = false; var failedFx = false;
            fxClient.SendCallback = args =>
            {
                var header = (uint)BinaryPrimitives.ReadUInt16BigEndian(args.MemoryBuffer.Span.Slice(4, 2));
                if (failSnapshot) throw new IOException("Injected native snapshot enqueue failure");
                if (header is >= 9473 and <= 9476)
                {
                    if (failFx) { failedFx = true; throw new IOException("Injected native FX enqueue failure"); }
                    sentFx.Add(header);
                }
                return true;
            };
            fxItem.SetState(0, 0, 0, []); roomVariables.InvalidateFx(); sentFx.Clear(); atomicDb.Commands = 0;
            nativeWired.OnCycle(); Assert.Empty(sentFx); Assert.Equal(0, atomicDb.Commands);
            failSnapshot = true; Assert.Throws<IOException>(() => liveRoom.SendObjects(fxClient)); failSnapshot = false;
            nativeWired.OnCycle(); Assert.Empty(sentFx); Assert.Empty(nativeWired.CaptureFxViewers());
            liveRoom.SendObjects(fxClient); Assert.Single(nativeWired.CaptureFxViewers());
            atomicDb.Commands = 0; nativeWired.OnCycle();
            Assert.Equal(new uint[] { 9473, 9475 }, sentFx);
            var nativeReadCommands = atomicDb.Commands; Assert.InRange(nativeReadCommands, 1, 5);
            sentFx.Clear(); atomicDb.Commands = 0; nativeWired.OnCycle();
            Assert.Empty(sentFx); Assert.Equal(0, atomicDb.Commands);
            roomVariables.Fx.RemoveViewer(fxPlayer.Id); roomVariables.InvalidateFx(); failFx = true;
            nativeWired.OnCycle(); Assert.True(failedFx); Assert.True(roomVariables.FxDirty); Assert.Empty(sentFx);
            failFx = false; nativeWired.OnCycle(); Assert.Equal(new uint[] { 9473, 9475 }, sentFx); sentFx.Clear();
            admin.Execute("UPDATE rooms SET owner='invalid-owner' WHERE id=@room", new { room }); roomVariables.InvalidateFx();
            nativeWired.OnCycle(); Assert.Contains(9476u, sentFx); sentFx.Clear();
            admin.Execute("UPDATE rooms SET owner=@owner WHERE id=@room", new { owner = owner.ToString(), room }); roomVariables.InvalidateFx();
            nativeWired.OnCycle(); Assert.Contains(9475u, sentFx); sentFx.Clear();
            var replacementViewer = new RoomUser(fxPlayer.Id, 0, fxUser.VirtualId, liveRoom);
            typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(replacementViewer, fxClient);
            liveUsers[replacementViewer.VirtualId] = replacementViewer;
            nativeWired.OnCycle(); Assert.Empty(sentFx); Assert.Empty(nativeWired.CaptureFxViewers());
            liveRoom.SendObjects(fxClient); nativeWired.OnCycle(); Assert.Equal(new uint[] { 9473, 9475 }, sentFx); sentFx.Clear();
            liveUsers[fxUser.VirtualId] = fxUser; liveRoom.SendObjects(fxClient); nativeWired.OnCycle(); sentFx.Clear();
            fxItem.SetState(1, 1, 0, []); nativeWired.OnCycle(); Assert.Equal(new uint[] { 9476 }, sentFx); sentFx.Clear();
            output.WriteLine($"Actual SQL + native Room.SendObjects/OnCycle FX: initial {nativeReadCommands} SQL commands; unchanged0; no pre-snapshot FX; failed snapshot/send retry; owner recheck; same-ID viewer replacement; detach-placement removal passed.");
            var global = Assert.IsType<WiredVariableDefinitionBox>(roomVariables.CreateBox(new Item
            { Id = globalItem, Definition = new() { InteractionName = "wf_var_room" } }));
            Assert.Same(global, WiredBoxLoading.Select(null, global, null));
            Assert.False(global.HasPersistedConfiguration);
            var engine = new WiredStackEngine(() => 5000, box => ReferenceEquals(box, global), _ => true, _ => { }, _ => { }); engine.Add(global);
            void SaveGlobal(WiredConfiguration candidate) => Assert.True(WiredConfigurationSave.TrySave(global, candidate, new RejectConfigurationStore(), out _, publish: engine.PublishConfigured));
            var original = new WiredConfiguration { IntParams = [10, 7], Text = "atomic" };
            SaveGlobal(original); roomVariables.ConfigurationSaved(global);
            Assert.Equal(7, roomVariables.Module.Read(new(WiredVariableTarget.Global, $"custom:{globalItem}"), new(WiredVariableTarget.Global, 0, 0), new(room, []))!.Value);
            roomVariables.DrainChanges();
            foreach (var prefix in new[] { "INSERT INTO wired_item_configurations", "INSERT INTO wired_variable_values" })
            {
                atomicDb.FailSqlPrefix = prefix;
                Assert.Throws<InjectedCommandFailure>(() => SaveGlobal(original with { IntParams = [10, 42], Text = "proposed" }));
                atomicDb.FailSqlPrefix = null;
                Assert.Same(original, global.Configuration);
                Assert.Equal("atomic", JsonSerializer.Deserialize<WiredConfiguration>(admin.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=@id", new { id = globalItem }))!.Text);
                Assert.Equal(7, store.Read(new(globalItem, WiredVariableTarget.Global, 0))!.Value);
                Assert.Empty(roomVariables.DrainChanges());
            }
            atomicDb.BeforeConnection = () => admin.Execute("UPDATE rooms SET owner='invalid-owner' WHERE id=@room", new { room });
            Assert.Throws<InvalidOperationException>(() => SaveGlobal(original with { IntParams = [10, 42] }));
            admin.Execute("UPDATE rooms SET owner=@owner WHERE id=@room", new { owner = owner.ToString(), room });
            Assert.Same(original, global.Configuration); Assert.Equal(7, store.Read(new(globalItem, WiredVariableTarget.Global, 0))!.Value); Assert.Empty(roomVariables.DrainChanges());
            var replacement = original with { Text = "external" };
            admin.Execute("UPDATE wired_item_configurations SET configuration=@configuration WHERE item_id=@id", new { id = globalItem, configuration = JsonSerializer.Serialize(replacement) });
            Assert.Throws<InvalidOperationException>(() => SaveGlobal(original with { IntParams = [10, 42] }));
            Assert.Equal("external", JsonSerializer.Deserialize<WiredConfiguration>(admin.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=@id", new { id = globalItem }))!.Text);
            Assert.Same(original, global.Configuration); Assert.Empty(roomVariables.DrainChanges());
            admin.Execute("UPDATE wired_item_configurations SET configuration=@configuration WHERE item_id=@id", new { id = globalItem, configuration = JsonSerializer.Serialize(original) });
            var accepted = original with { IntParams = [10, 42], Text = "accepted" };
            SaveGlobal(accepted); roomVariables.ConfigurationSaved(global);
            Assert.Equal(42, store.Read(new(globalItem, WiredVariableTarget.Global, 0))!.Value); Assert.Single(roomVariables.DrainChanges());
            var activeConfiguration = accepted with { IntParams = [1, 21] };
            SaveGlobal(activeConfiguration); roomVariables.DrainChanges();
            atomicDb.FailSqlPrefix = "INSERT INTO wired_item_configurations";
            Assert.Throws<InjectedCommandFailure>(() => SaveGlobal(activeConfiguration with { IntParams = [1, 31] }));
            atomicDb.FailSqlPrefix = null;
            Assert.Same(activeConfiguration, global.Configuration);
            Assert.Equal(21, roomVariables.Module.Read(new(WiredVariableTarget.Global, $"custom:{globalItem}"), new(WiredVariableTarget.Global, 0, 0), new(room, []))!.Value);
            Assert.Empty(roomVariables.DrainChanges());

            var firstSaveItem = Insert(admin, "items", new() { ["user_id"] = owner, ["room_id"] = room, ["base_item"] = baseItem, ["extra_data"] = "", ["wall_pos"] = "" }); items.Add(firstSaveItem);
            var firstSaveModules = Enumerable.Range(0, 2).Select(_ => new WiredRoomVariables(liveRoom, new ProbeDatabase(connectionString), () => 6000)).ToArray();
            var firstSaveBoxes = firstSaveModules.Select(m => Assert.IsType<WiredVariableDefinitionBox>(m.CreateBox(new Item { Id = firstSaveItem, Definition = new() { InteractionName = "wf_var_room" } }))).ToArray();
            var firstSaves = await Task.WhenAll(firstSaveBoxes.Select((box, index) => Task.Run(() =>
            {
                var candidate = new WiredConfiguration { IntParams = [10, 100 + index], Text = "first" + index };
                try { return WiredConfigurationSave.TrySave(box, candidate, new RejectConfigurationStore(), out _); }
                catch (InvalidOperationException exception) when (exception.Message == "The variable configuration changed before saving.") { return false; }
            })));
            Assert.Single(firstSaves.Where(x => x));
            Assert.Equal(1, firstSaveModules.Sum(x => x.DrainChanges().Count));
            Assert.Equal(100 + Array.IndexOf(firstSaves, true), store.Read(new(firstSaveItem, WiredVariableTarget.Global, 0))!.Value);
            output.WriteLine("Atomic definition/global save: configuration-command failure, value-command failure, owner race and stale sidecar rejected without active publication or events; successful combined commit passed.");
            output.WriteLine("Active global rollback and concurrent first sidecar/value save passed: one winner, one rejected stale save, one event.");

            var levelItem = new Item { Id = 1100000000, Definition = new() { InteractionName = "wf_xtra_var_lvlup_system" } };
            var timeItem = new Item { Id = 1100000001, Definition = new() { InteractionName = "wf_xtra_var_time_util" } };
            floor[levelItem.Id] = levelItem; floor[timeItem.Id] = timeItem;
            var levelBox = Assert.IsType<WiredVariableMetadataBox>(roomVariables.CreateBox(levelItem));
            levelBox.ApplyConfiguration(new() { Text = "{\"mode\":1,\"stepSize\":10,\"maxLevel\":10,\"subvariables\":[0,2]}" });
            roomVariables.ConfigurationLoaded(levelBox);
            var timeBox = Assert.IsType<WiredVariableMetadataBox>(roomVariables.CreateBox(timeItem));
            timeBox.ApplyConfiguration(new() { IntParams = [(1 << 2) | (1 << 21), 1] }); roomVariables.ConfigurationLoaded(timeBox);
            var levelId = WiredRoomVariables.SyntheticId(WiredVariableTarget.User, items[0], 0, false)!.Value;
            var timeId = WiredRoomVariables.SyntheticId(WiredVariableTarget.User, items[0], 21, true)!.Value;
            var levelRef = new WiredVariableReference(WiredVariableTarget.User, $"custom:{levelId}");
            Assert.Equal(3, roomVariables.Module.Read(levelRef, holders[0], frame)!.Value);
            Assert.Equal(1, roomVariables.Module.Read(new(WiredVariableTarget.User, $"custom:{timeId}"), holders[0], frame)!.Value);
            Assert.False(roomVariables.Module.Mutate(levelRef, holders[0], WiredVariableMutation.Set, 99, frame));
            Assert.Contains(roomVariables.Catalog().Variables, x => x.Definition.ItemId == levelId && x.ReadOnly && !x.CanReadTimestamps);
            atomicDb.Commands = 0;
            using (var reads = roomVariables.Module.CaptureReads([levelRef, new(WiredVariableTarget.User, $"custom:{timeId}")], frame))
                foreach (var holder in holders) Assert.Equal(3, reads.Read(levelRef, holder, frame)!.Value);
            Assert.Equal(3, atomicDb.Commands); // One owner, one base definition, one bulk value query for both derived fields.
            admin.Execute("UPDATE rooms SET owner=@changed WHERE id=@room", new { changed = "0", room });
            Assert.Null(roomVariables.Module.Read(levelRef, holders[0], frame));
            admin.Execute("UPDATE rooms SET owner=@owner WHERE id=@room", new { owner = owner.ToString(), room });
            levelItem.SetState(1, 0, 0, []);
            Assert.Null(roomVariables.Module.Read(levelRef, holders[0], frame));
            var questItem = new Item { Id = 1100000002, Definition = new() { InteractionName = "wf_var_quest" } }; floor[questItem.Id] = questItem;
            var questBox = Assert.IsType<WiredVariableMetadataBox>(roomVariables.CreateBox(questItem));
            questBox.ApplyConfiguration(new() { IntParams = [50] }); roomVariables.ConfigurationLoaded(questBox);
            Assert.Equal(25, roomVariables.Module.Read(levelRef, holders[0], frame)!.Value);
            Assert.Contains(roomVariables.Catalog().Variables, x => x.Definition.ItemId == levelId && x.Definition.Name == "probe0.progress");
            Assert.Empty(store.GetHolders(levelId)); Assert.Empty(store.GetHolders(timeId));
            roomVariables.ItemDetached(questItem); floor.TryRemove(questItem.Id, out _);
            roomVariables.ItemDetached(levelItem); roomVariables.ItemDetached(timeItem); floor.TryRemove(levelItem.Id, out _); floor.TryRemove(timeItem.Id, out _);
            output.WriteLine("Derived level/time catalog and public reads: real base values, read-only mutation rejection, 400 derived values in 3 SQL commands, owner revocation and moved metadata invalidation passed.");

            var contextItemId = Insert(admin, "items", new() { ["user_id"] = owner, ["room_id"] = room, ["base_item"] = baseItem, ["extra_data"] = "", ["wall_pos"] = "" }); items.Add(contextItemId);
            var contextConfiguration = new WiredConfiguration { IntParams = [1], Text = "captured" };
            admin.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES (@id,'wf_var_context',1,@config)",
                new { id = contextItemId, config = JsonSerializer.Serialize(contextConfiguration) });
            var contextItem = new Item { Id = contextItemId, Definition = new() { InteractionName = "wf_var_context" } }; floor[contextItemId] = contextItem;
            var contextBox = roomVariables.CreateBox(contextItem)!; contextBox.ApplyConfiguration(contextConfiguration); roomVariables.ConfigurationLoaded(contextBox);
            var captureItem = new Item { Id = 1100000003, Definition = new() { InteractionName = "wf_xtra_text_input_variable" } }; floor[captureItem.Id] = captureItem;
            var captureBox = Assert.IsType<WiredVariableTextInputBox>(roomVariables.CreateBox(captureItem));
            Assert.True(captureBox.TryValidateConfiguration(new() { IntParams = [1], Text = $"{contextItemId}\tamount" }, out var captureConfiguration, out _));
            captureBox.ApplyConfiguration(captureConfiguration); roomVariables.ConfigurationLoaded(captureBox);
            Assert.True(WiredBoxRegistry.TryGet("wf_trg_says_something", out var speechDescriptor));
            var speechTrigger = new WiredModernTrigger(liveRoom, new Item { Id = 1100000004 }, speechDescriptor);
            speechTrigger.ApplyConfiguration(new() { IntParams = [1, 0, 0], Text = "set #amount#" });
            var speech = new WiredRuntimeContext(liveRoom, new(WiredEventKind.Speech) { Actor = fxUser, Message = "set 42" },
                new(() => floor.Values.ToArray(), () => [fxUser]), new UnusedOperations());
            Assert.True(roomVariables.CaptureSpeech(speech, speechTrigger));
            Assert.Equal(42, roomVariables.Module.Read(new(WiredVariableTarget.Context, $"custom:{contextItemId}"), new(WiredVariableTarget.Context, 0, 0), speech.VariableFrame!)!.Value);
            var unrelated = new WiredVariableFrame(room, []);
            Assert.Null(roomVariables.Module.Read(new(WiredVariableTarget.Context, $"custom:{contextItemId}"), new(WiredVariableTarget.Context, 0, 0), unrelated));
            speechTrigger.ApplyConfiguration(new() { IntParams = [1, 0, 1], Text = "set #amount#" });
            var forbiddenSpeech = new WiredRuntimeContext(liveRoom, speech.Event, speech.Targets, new UnusedOperations());
            Assert.False(roomVariables.CaptureSpeech(forbiddenSpeech, speechTrigger)); Assert.Null(forbiddenSpeech.VariableFrame);
            Assert.Empty(store.GetHolders(contextItemId));
            roomVariables.ItemDetached(captureItem); floor.TryRemove(captureItem.Id, out _);
            Assert.Null(roomVariables.CaptureSpeech(speech, speechTrigger));
            output.WriteLine("Actual room text capture: current authorized context definition receives42, unrelated firing empty, owner-only rejection before publication, detached capturer falls back, no durable context rows.");

            var clearDb = new ProbeDatabase(connectionString);
            var clearModule = new WiredVariableModule(room, directory, new DatabaseWiredVariableStore(clearDb), () => 7000);
            clearDb.FailSqlPrefix = "DELETE FROM wired_variable_values";
            Assert.Throws<InjectedCommandFailure>(() => clearModule.ClearValues(items[1], WiredVariableTarget.User, frame));
            clearDb.FailSqlPrefix = null;
            Assert.Equal(200, store.GetHolders(items[1]).Count); Assert.Empty(clearModule.DrainChanges());
            Assert.Equal(200, clearModule.ClearValues(items[1], WiredVariableTarget.User, frame));
            Assert.Empty(store.GetHolders(items[1])); Assert.Equal(200, clearModule.DrainChanges().Count);
            Assert.True(clearModule.Mutate(new(WiredVariableTarget.User, $"custom:{items[1]}"), holders[0], WiredVariableMutation.Give, 7, frame));
            Assert.Equal(7, store.Read(new(items[1], WiredVariableTarget.User, holders[0].StableId))!.Value);
            output.WriteLine("Atomic owner-only clear: injected DELETE failure retained all 200 rows with no events; successful clear removed 200 and retained assignable definition.");

            // The real room adapter must detach two explicit temporary identities independently.
            var temporaryItems = new[] { new Item { Id = uint.MaxValue, IsTemporary = true, Definition = new() },
                new Item { Id = uint.MaxValue - 1, IsTemporary = true, Definition = new() } };
            foreach (var temporary in temporaryItems) floor[temporary.Id] = temporary;
            var temporaryHolders = temporaryItems.Select(WiredVariableRuntimeFrames.FurniHolder).ToArray();
            var temporaryFrame = new WiredVariableFrame(room, temporaryHolders);
            var temporaryReference = new WiredVariableReference(WiredVariableTarget.Furni, $"custom:{items[2]}");
            admin.Execute("UPDATE wired_item_configurations SET box_name='wf_var_furni',configuration=@config WHERE item_id=@id",
                new { id = items[2], config = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 10], Text = "tempProbe" }) });
            atomicDb.FailSqlPrefix = "INSERT INTO wired_variable_values";
            foreach (var holder in temporaryHolders)
            {
                Assert.False(roomVariables.Module.Mutate(temporaryReference, holder, WiredVariableMutation.Give, 77, temporaryFrame));
                Assert.Null(roomVariables.Module.Read(temporaryReference, holder, temporaryFrame));
            }
            atomicDb.FailSqlPrefix = null;
            Assert.DoesNotContain(store.GetHolders(items[2]).Keys, key => key.HolderId <= 0);
            admin.Execute("UPDATE wired_item_configurations SET configuration=@config WHERE item_id=@id",
                new { id = items[2], config = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 1], Text = "tempProbe" }) });
            Assert.True(roomVariables.Module.Mutate(temporaryReference, temporaryHolders[0], WiredVariableMutation.Give, 10, temporaryFrame));
            Assert.True(roomVariables.Module.Mutate(temporaryReference, temporaryHolders[1], WiredVariableMutation.Give, 20, temporaryFrame));
            roomVariables.ItemDetached(temporaryItems[0]); floor.TryRemove(temporaryItems[0].Id, out _);
            Assert.Null(roomVariables.Module.Read(temporaryReference, temporaryHolders[0], temporaryFrame));
            Assert.Equal(20, roomVariables.Module.Read(temporaryReference, temporaryHolders[1], temporaryFrame)!.Value);
            roomVariables.ItemDetached(temporaryItems[1].Id); floor.TryRemove(temporaryItems[1].Id, out _);
            Assert.Null(roomVariables.Module.Read(temporaryReference, temporaryHolders[1], temporaryFrame));
            roomVariables.ItemDetached(uint.MaxValue); // Already detached: never classify from sign or throw.
            output.WriteLine("Two actual marked temporary room items: independent active values, durable insert rejected, Item/uint detach cleaned both without signed-ID overflow.");

            Assert.Equal(200, module.DeleteDefinition(items[0]));
            Assert.Empty(store.GetHolders(items[0]));
            Assert.Throws<InvalidOperationException>(() => module.Mutate(reference, holders[0], WiredVariableMutation.Give, 7, frame));
            Assert.DoesNotContain(module.DrainChanges(), x => x.After?.Value == 7);
            output.WriteLine("Persistence/reconnect, 40 concurrent increments, concurrent creation, transactional ownership/configuration/placement rejection and deletion tombstone passed.");
        }
        finally
        {
            foreach (var id in items)
            {
                admin.Execute("DELETE FROM wired_variable_values WHERE definition_id=@id", new { id });
                admin.Execute("DELETE FROM wired_variable_locks WHERE definition_id=@id", new { id });
                admin.Execute("DELETE FROM wired_item_configurations WHERE item_id=@id", new { id });
                admin.Execute("DELETE FROM items WHERE id=@id", new { id });
            }
            foreach (var id in rooms) admin.Execute("DELETE FROM rooms WHERE id=@id", new { id });
            foreach (var id in users) admin.Execute("DELETE FROM users WHERE id=@id", new { id });
            output.WriteLine("All disposable probe rows removed.");
        }
    }

    private static string GuardedConnectionString()
    {
        using var process = Process.Start(new ProcessStartInfo("docker")
        { ArgumentList = { "inspect", "plus-wired-preview-db-1" }, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var inspect = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        using var json = JsonDocument.Parse(inspect); var container = json.RootElement[0];
        Assert.Equal("plus-wired-preview", container.GetProperty("Config").GetProperty("Labels").GetProperty("com.docker.compose.project").GetString());
        var networks = container.GetProperty("NetworkSettings").GetProperty("Networks");
        Assert.Single(networks.EnumerateObject()); Assert.True(networks.TryGetProperty("plus-wired-preview_default", out var network));
        Assert.Contains(container.GetProperty("Mounts").EnumerateArray(), m => m.TryGetProperty("Name", out var name) && name.GetString() == "plus-wired-preview_wired-db-data" && m.GetProperty("Destination").GetString() == "/var/lib/mysql");
        using var config = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("WIRED_VARIABLE_PREVIEW_CONFIG")!));
        var db = config.RootElement.GetProperty("Database");
        return new MySqlConnectionStringBuilder
        {
            Server = network.GetProperty("IPAddress").GetString(),
            Port = 3306,
            Database = db.GetProperty("Name").GetString(),
            UserID = db.GetProperty("Username").GetString(),
            Password = db.GetProperty("Password").GetString(),
            MinimumPoolSize = 0,
            MaximumPoolSize = 8
        }.ConnectionString;
    }

    private static uint Insert(MySqlConnection connection, string table, Dictionary<string, object> values)
    {
        var columns = connection.Query<Column>("SELECT COLUMN_NAME AS Name,DATA_TYPE AS Type,COLUMN_TYPE AS FullType FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table AND IS_NULLABLE='NO' AND COLUMN_DEFAULT IS NULL AND EXTRA NOT LIKE '%auto_increment%'", new { table });
        foreach (var column in columns.Where(x => !values.ContainsKey(x.Name)))
            values[column.Name] = column.Type switch
            {
                "enum" => column.FullType.Split('\'')[1],
                "datetime" or "timestamp" or "date" => DateTime.UtcNow,
                "varchar" or "char" or "text" or "mediumtext" or "longtext" => "",
                _ => 0
            };
        var parameters = new DynamicParameters(); foreach (var value in values) parameters.Add(value.Key, value.Value);
        connection.Execute($"INSERT INTO `{table}` ({string.Join(',', values.Keys.Select(x => $"`{x}`"))}) VALUES ({string.Join(',', values.Keys.Select(x => "@" + x))})", parameters);
        return connection.ExecuteScalar<uint>("SELECT LAST_INSERT_ID()");
    }
    private sealed class Column { public string Name { get; set; } = ""; public string Type { get; set; } = ""; public string FullType { get; set; } = ""; }
    private sealed class RejectConfigurationStore : IWiredConfigurationStore
    {
        public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor) => throw new InvalidOperationException("Combined provider must own persistence.");
        public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration) => throw new InvalidOperationException("Combined provider must own persistence.");
    }
    private sealed class UnusedOperations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public int Commands;
        public Action? BeforeConnection;
        public string? FailSqlPrefix;
        public bool IsConnected() => true;
        public IDbConnection Connection()
        {
            var action = BeforeConnection; BeforeConnection = null; action?.Invoke();
            return new CountedConnection(new MySqlConnection(connectionString), () => Interlocked.Increment(ref Commands), sql =>
            {
                if (FailSqlPrefix is { } prefix && sql.TrimStart().StartsWith(prefix, StringComparison.Ordinal)) throw new InjectedCommandFailure();
            });
        }
    }
    private sealed class InjectedCommandFailure : Exception;
    private sealed class CountedConnection(MySqlConnection inner, Action command, Action<string> execute) : IDbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value ?? ""; }
        public int ConnectionTimeout => inner.ConnectionTimeout; public string Database => inner.Database; public ConnectionState State => inner.State;
        public IDbTransaction BeginTransaction() => inner.BeginTransaction(); public IDbTransaction BeginTransaction(IsolationLevel level) => inner.BeginTransaction(level);
        public void ChangeDatabase(string name) => inner.ChangeDatabase(name); public void Close() => inner.Close(); public void Open() => inner.Open(); public void Dispose() => inner.Dispose();
        public IDbCommand CreateCommand() { command(); return new InterceptedCommand(inner.CreateCommand(), execute); }
    }
    private sealed class InterceptedCommand(IDbCommand inner, Action<string> execute) : IDbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string CommandText { get => inner.CommandText; set => inner.CommandText = value ?? ""; }
        public int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
        public CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
        public IDbConnection? Connection { get => inner.Connection; set => inner.Connection = value; }
        public IDataParameterCollection Parameters => inner.Parameters;
        public IDbTransaction? Transaction { get => inner.Transaction; set => inner.Transaction = value; }
        public UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }
        public void Cancel() => inner.Cancel(); public IDbDataParameter CreateParameter() => inner.CreateParameter(); public void Dispose() => inner.Dispose();
        public void Prepare() => inner.Prepare();
        public int ExecuteNonQuery() { execute(CommandText); return inner.ExecuteNonQuery(); }
        public object? ExecuteScalar() { execute(CommandText); return inner.ExecuteScalar(); }
        public IDataReader ExecuteReader() { execute(CommandText); return inner.ExecuteReader(); }
        public IDataReader ExecuteReader(CommandBehavior behavior) { execute(CommandText); return inner.ExecuteReader(behavior); }
    }
}
