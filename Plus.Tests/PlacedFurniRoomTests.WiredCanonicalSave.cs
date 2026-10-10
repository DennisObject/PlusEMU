using System.Reflection;
using System.Collections.Immutable;
using Dapper;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CanonicalNativeScoreSaveOwnsQuotaInsteadOfPreviousConfiguration(int quota)
    {
        var (box, store) = CanonicalBox("wf_act_give_score", new() { IntParams = [17, 0, 0], ScoreQuotaPerGame = 1 });
        await CanonicalHandler().Parse(_client, ClientPacket(601, 2, 17, quota, "", 0, 0, 0, 1, 0, 0, 0));
        var saved = Assert.Single(store.Saves);
        Assert.Equal(quota == 0 ? null : (int?)quota, saved.ScoreQuotaPerGame);
        Assert.Equal(saved, box.Configuration);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
    }

    [Fact]
    public async Task CanonicalNativeClockSourceComesFromCountedTail()
    {
        var (box, store) = CanonicalBox("wf_act_control_clock", new() { IntParams = [0, 100] });
        await CanonicalHandler().Parse(_client, ClientPacket(601, 1, 0, "", 0, 0, 1, 201, 0, 0, 0));
        Assert.Equal(201, Assert.Single(store.Saves).FurniSources["items"]);
        Assert.Equal(201, box.Configuration.FurniSources["items"]);
    }

    [Fact]
    public void CanonicalPublicSaveRefusesAnUnboundRuntimeCache()
    {
        var (box, store) = CanonicalBox("wf_act_control_clock", new() { IntParams = [0, 100] });
        var previous = box.Configuration;
        CanonicalService(store).Save(_client, new(601, WiredBoxCategory.Action, previous with { IntParams = [1, 100] }));
        Assert.Empty(store.Saves);
        Assert.Same(previous, box.Configuration);
    }

    [Fact]
    public void CanonicalPublicUnchangedSaveNeverPersistsOrPublishes()
    {
        var (box, store) = CanonicalBox("wf_act_control_clock", new() { IntParams = [0, 100] });
        var previous = box.Configuration;
        CanonicalService(store).Save(_client, new(601, WiredBoxCategory.Action, previous));
        Assert.Empty(store.Saves);
        Assert.Same(previous, box.Configuration);
    }







    [Fact]
    public async Task CanonicalNativeUnchangedSaveKeepsPendingActionsAndPublicationState()
    {
        var (box, store) = CanonicalBox("wf_act_control_clock", new() { IntParams = [0, 100], Delay = 5 });
        var native = new WiredNativeEditorConfiguration
        {
            Category = WiredBoxCategory.Action,
            NativeCode = 28,
            OwnedIntParams = [0],
            FurniSourceTypes = [100],
            Delay = 5
        };
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, native, out var current));
        box.ApplyConfiguration(current);
        AddSpeechBox(603, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "pulse" }, 0, 0, 0);
        var engine = (WiredStackEngine)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent)
            .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetWired())!;
        Assert.True(engine.Enqueue(new(WiredEventKind.Speech) { Message = "pulse" }));
        var pending = engine.ReadStats().Pending;
        Assert.True(pending > 0);
        var published = 0;
        engine.ConfigurationPublished = _ => published++;
        await CanonicalHandler().Parse(_client, ClientPacket(601, 1, 0, "", 0, 5, 1, 100, 0, 0, 0));
        Assert.Empty(store.Saves);
        Assert.Same(current, box.Configuration);
        Assert.Equal(pending, engine.ReadStats().Pending);
        Assert.Equal(0, published);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
    }

    [Theory]
    [InlineData("params")]
    [InlineData("sources")]
    [InlineData("quota")]
    [InlineData("version")]
    [InlineData("stripped")]
    [InlineData("authority")]
    [InlineData("owner")]
    public void CanonicalAlteredNativeCacheCannotBecomeLegacyAtSaveApplyOrValidation(string change)
    {
        var (box, store) = CanonicalBox("wf_act_control_clock", new() { IntParams = [0, 100] });
        var native = new WiredNativeEditorConfiguration
        {
            Category = WiredBoxCategory.Action,
            NativeCode = 28,
            OwnedIntParams = [0],
            FurniSourceTypes = [100],
            Delay = 0
        };
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, native, out var valid));
        box.ApplyConfiguration(valid);
        var invalid = change switch
        {
            "params" => valid with { IntParams = [1, 100] },
            "sources" => valid with { FurniSources = ImmutableDictionary<string, int>.Empty.Add("items", 201) },
            "quota" => valid with { ScoreQuotaPerGame = 2 },
            "version" => valid with { Version = 2 },
            "stripped" => new WiredConfiguration { IntParams = valid.IntParams, FurniSources = valid.FurniSources },
            "authority" => valid.Bind(valid.Origin! with { Native = null }),
            "owner" => valid.Bind(valid.Origin! with { ItemId = 602 }),
            _ => throw new ArgumentException(change)
        };
        Assert.False(box.TryValidateConfiguration(invalid, out _, out _));
        Assert.False(WiredConfigurationSave.TrySave(box, invalid, store, out _));
        Assert.Throws<InvalidDataException>(() => box.ApplyConfiguration(invalid));
        Assert.Empty(store.Saves);
        Assert.Same(valid, box.Configuration);
        var context = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        var captured = (Dictionary<uint, WiredConfiguration>)typeof(WiredRuntimeContext)
            .GetField("_configurations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        captured[601] = invalid;
        Assert.False(((IWiredContextualAction)box).Execute(context));
    }


    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(201)]
    public void CanonicalGroupEachAdvertisedMoverRoleExecutesAgainstItsActualDomain(int moverSource)
    {
        var (box, _) = CanonicalBox("wf_act_move_furni_as_group", new() { IntParams = [0, 100] });
        var mover = CanonicalPlace(602, 1, 1);
        var target = CanonicalPlace(603, 2, 1);
        var native = CanonicalNativeGroup(moverSource, 101);
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, native, out var configuration));
        box.ApplyConfiguration(configuration);
        var context = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        context.Triggering.FurniIds.Add(mover.Id);
        context.SelectorPool.FurniIds.Add(mover.Id);
        context.Signal = new(new([mover.Id]), new Dictionary<string, long>());
        var changed = false;
        _room.RunFastPass(() => changed = ((IWiredContextualAction)box).Execute(context));
        Assert.True(changed);
        Assert.Equal(3, mover.GetX);
        Assert.Equal(2, target.GetX);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(100, 2)]
    [InlineData(101, 3)]
    [InlineData(200, 3)]
    [InlineData(201, 3)]
    public void CanonicalGroupTargetRoleOneHundredUsesPrimaryAndOneOhOneUsesSecondary(int targetSource, int expectedX)
    {
        var (box, _) = CanonicalBox("wf_act_move_furni_as_group", new() { IntParams = [0, 100] });
        var mover = CanonicalPlace(602, 1, 1);
        var target = CanonicalPlace(603, 2, 1);
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, CanonicalNativeGroup(100, targetSource), out var configuration));
        box.ApplyConfiguration(configuration);
        var context = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        context.Triggering.FurniIds.Add(target.Id);
        context.SelectorPool.FurniIds.Add(target.Id);
        context.Signal = new(new([target.Id]), new Dictionary<string, long>());
        var changed = false;
        _room.RunFastPass(() => changed = ((IWiredContextualAction)box).Execute(context));
        Assert.True(changed);
        Assert.Equal(expectedX, mover.GetX);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(201)]
    public void CanonicalGroupDynamicTargetNeverMovesTowardAWall(int targetSource)
    {
        var (box, _) = CanonicalBox("wf_act_move_furni_as_group", new() { IntParams = [0, 100] });
        var mover = CanonicalPlace(602, 1, 1);
        var wall = Furni(604, InteractionType.None, WiredBoxType.None, ItemType.Wall);
        wall.WallCoordinates = ":w=2,1 l=12,16 l";
        Assert.True(_room.GetRoomItemHandler().SetWallItem(_client, wall));
        var native = CanonicalNativeGroup(100, targetSource) with { SecondaryItems = [] };
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, native, out var configuration));
        box.ApplyConfiguration(configuration);
        var context = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        context.Triggering.FurniIds.Add(wall.Id);
        context.SelectorPool.FurniIds.Add(wall.Id);
        context.Signal = new(new([wall.Id]), new Dictionary<string, long>());
        var changed = true;
        _room.RunFastPass(() => changed = ((IWiredContextualAction)box).Execute(context));
        Assert.False(changed);
        Assert.Equal(1, mover.GetX);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(201)]
    public void CanonicalGroupEachAdvertisedUserRoleUsesTheCapturedAvatar(int source)
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        var (box, _) = CanonicalBox("wf_act_move_furni_as_group", new() { IntParams = [0, 100] });
        var mover = CanonicalPlace(602, 1, 1);
        var native = CanonicalNativeGroup(100, 101) with { OwnedIntParams = [1, 1, 0], UserSourceTypes = [source], SecondaryItems = [] };
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, native, out var configuration));
        box.ApplyConfiguration(configuration);
        var context = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        context.Triggering.UserIds.Add(actor.VirtualId);
        context.SelectorPool.UserIds.Add(actor.VirtualId);
        context.Signal = new(new([], [actor.VirtualId]), new Dictionary<string, long>());
        var changed = false;
        _room.RunFastPass(() => changed = ((IWiredContextualAction)box).Execute(context));
        Assert.True(changed);
        Assert.Equal(actor.X + 1, mover.GetX);
        Assert.Equal(actor.Y, mover.GetY);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanonicalGroupEmptyOrReplacedCapturedMoversDoNotMove(bool replace)
    {
        var (box, _) = CanonicalBox("wf_act_move_furni_as_group", new() { IntParams = [0, 100] });
        var mover = CanonicalPlace(602, 1, 1);
        CanonicalPlace(603, 2, 1);
        var native = CanonicalNativeGroup(100, 101) with { PrimaryItems = replace ? [new(602, false)] : [] };
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, native, out var configuration));
        box.ApplyConfiguration(configuration);
        var context = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;

        if (replace) {
            context.FurniIdentity[602] = Furni(602, InteractionType.None, WiredBoxType.None);
        }

        var changed = true;
        _room.RunFastPass(() => changed = ((IWiredContextualAction)box).Execute(context));
        Assert.False(changed);
        Assert.Equal(1, mover.GetX);
    }

    [Fact]
    public async Task CanonicalNativeScoreQuotaIsConsumedAndUnchangedSaveDoesNotResetIt()
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        actor.Team = Plus.HabboHotel.Rooms.Games.Teams.Team.Red;
        var (box, store) = CanonicalBox("wf_act_give_score", new() { IntParams = [1, 0, 0] });
        await CanonicalHandler().Parse(_client, ClientPacket(601, 2, 17, 1, "", 0, 0, 0, 1, 0, 0, 0));
        var context = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        context.Triggering.UserIds.Add(actor.VirtualId);
        var changed = false;
        _room.RunFastPass(() => changed = ((IWiredContextualAction)box).Execute(context));
        Assert.True(changed);
        var score = _room.GetGameManager().Points[1];
        Assert.Equal(17, score);
        await CanonicalHandler().Parse(_client, ClientPacket(601, 2, 17, 1, "", 0, 0, 0, 1, 0, 0, 0));
        Assert.Single(store.Saves);
        _room.RunFastPass(() => changed = ((IWiredContextualAction)box).Execute(context));
        Assert.False(changed);
        Assert.Equal(score, _room.GetGameManager().Points[1]);
    }

    [Fact]
    public async Task CanonicalNativeAltitudeSaveReopenAndRuntimeRetainBothWallPickArrays()
    {
        var (box, store) = CanonicalBox("wf_act_set_altitude", new() { IntParams = [2, 100], Text = "0" });
        var floor = CanonicalPlace(602, 1, 1);
        var wall = Furni(604, InteractionType.None, WiredBoxType.None, ItemType.Wall);
        wall.WallCoordinates = ":w=2,1 l=12,16 l";
        Assert.True(_room.GetRoomItemHandler().SetWallItem(_client, wall));
        await CanonicalHandler().Parse(_client, ClientPacket(601, 2, 201, 2, "", 1, 602, 0, 1, 100, 0, 0, 1, -604));
        var saved = Assert.Single(store.Saves);
        Assert.Equal(new uint[] { 604 }, saved.SecondarySelectedItems);
        var snapshot = WiredEditorSnapshot.Capture(box);
        Assert.Equal(new[] { 201, 2 }, snapshot.Native!.OwnedIntParams);
        Assert.True(Assert.Single(snapshot.Native.SecondaryItems).Wall);
        var context = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        var changed = false;
        _room.RunFastPass(() => changed = ((IWiredContextualAction)box).Execute(context));
        Assert.True(changed);
        Assert.Equal(2.01, floor.GetZ, 6);
        Assert.Equal(":w=2,1 l=12,16 l", wall.WallCoordinates);
    }

    [Fact]
    public void CanonicalNoopAdmissionRefusesAConcurrentConfigurationChangeDuringRightsRecheck()
    {
        var (box, _) = CanonicalBox("wf_act_control_clock", new() { IntParams = [0, 100] });
        var native = new WiredNativeEditorConfiguration
        {
            Category = WiredBoxCategory.Action,
            NativeCode = 28,
            OwnedIntParams = [0],
            FurniSourceTypes = [100],
            Delay = 0
        };
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, native, out var first));
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, native with { OwnedIntParams = [1] }, out var second));
        box.ApplyConfiguration(first);
        var checks = 0;
        var admission = _room.GetWired().TryAdmitUnchangedNativeSave(box, native, () =>
        {
            if (++checks == 2) {
                box.ApplyConfiguration(second);
            }

            return true;
        });
        Assert.Equal(WiredNativeSaveAdmission.Refused, admission);
        Assert.Same(second, box.Configuration);
    }

    [Fact]
    public async Task CanonicalUnrepresentableLegacyJoinModeCannotBeEditedButStillExecutes()
    {
        var (_, actor) = PrepareSpeech(new SpeechClock());
        _client.GetHabbo().Effects = new Plus.HabboHotel.Users.Effects.EffectsComponent(new SpeechClock());
        var (box, store) = CanonicalBox("wf_act_join_team", new() { IntParams = [0, 1, 0, 1] });
        var previous = box.Configuration;
        box.Item.Interactor.OnTrigger(_client, box.Item, 0, true);
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredEffectConfigComposer);
        await CanonicalHandler().Parse(_client, ClientPacket(601, 2, 1, 0, "", 0, 0, 0, 1, 0, 0, 0));
        Assert.Empty(store.Saves);
        Assert.Same(previous, box.Configuration);
        var context = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        context.Triggering.UserIds.Add(actor.VirtualId);
        var changed = false;
        _room.RunFastPass(() => changed = ((IWiredContextualAction)box).Execute(context));
        Assert.True(changed);
        Assert.NotEqual(Plus.HabboHotel.Rooms.Games.Teams.Team.None, actor.Team);
    }

    [Theory]
    [InlineData(100, 100, 602, 602)]
    [InlineData(100, 101, 602, 603)]
    [InlineData(101, 100, 603, 602)]
    [InlineData(101, 101, 603, 603)]
    [InlineData(100, 0, 602, 603)]
    [InlineData(100, 200, 602, 603)]
    [InlineData(100, 201, 602, 603)]
    public void CanonicalSignalAntennaAndForwardedRolesResolveTheirOwnPrimarySecondaryOrLiveDomains(int antennaSource, int forwardedSource, uint expectedAntenna, uint expectedForwarded)
    {
        var (box, _) = CanonicalBox("wf_act_send_signal", new() { IntParams = [0, 100, 0, 0, 0, 0] });
        var primary = CanonicalPlace(602, 1, 1);
        var secondary = CanonicalPlace(603, 2, 1);
        primary.Definition.InteractionName = "antenna";
        secondary.Definition.InteractionName = "antenna";
        var native = new WiredNativeEditorConfiguration
        {
            Category = WiredBoxCategory.Action,
            NativeCode = 30,
            OwnedIntParams = [0, 0],
            PrimaryItems = [new(602, false)],
            SecondaryItems = [new(603, false)],
            FurniSourceTypes = [antennaSource, forwardedSource],
            UserSourceTypes = [0],
            Delay = 0
        };
        Assert.True(WiredNativeEditorProjection.TryCompile(601, box.Descriptor, native, out var configuration));
        Assert.True(box.TryValidateConfiguration(configuration, out _, out _));
        box.ApplyConfiguration(configuration);
        var captured = _room.GetWired().CaptureVariableInspectionFrame().RuntimeContext!;
        var operations = new CanonicalSignals();
        var context = new WiredRuntimeContext(_room, new(WiredEventKind.Inspection), captured.Targets, operations);
        context.Triggering.FurniIds.Add(603);
        context.SelectorPool.FurniIds.Add(603);
        context.Signal = new(new([603]), new Dictionary<string, long>());
        Assert.True(((IWiredContextualAction)box).Execute(context));
        Assert.Equal(new[] { expectedAntenna }, operations.Antennas);
        Assert.Equal(new[] { expectedForwarded }, operations.Forwarded);
    }

    private sealed class CanonicalSignals : IWiredRuntimeOperations
    {
        public uint[] Antennas { get; private set; } = [];
        public uint[] Forwarded { get; private set; } = [];
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false)
        {
            Antennas = receivers.Select(item => item.Id).ToArray();
            Forwarded = selection.FurniIds.ToArray();

            return true;
        }
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new InvalidOperationException("Unexpected Call");
        public void ResetTimers(IEnumerable<Item> targets) => throw new InvalidOperationException("Unexpected timer reset");
    }

    [WiredChestDatabaseFact]
    public async Task CanonicalNativeUnmappedContractRefusalKeepsStoredContractAndRuntimeUntouched()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var (box, store) = CanonicalBox("wf_act_control_clock", new() { IntParams = [0, 100] });
        var current = box.Configuration;
        var contract = CanonicalPlace(608, 2, 1);
        contract.Definition.InteractionType = InteractionType.WiredContractPayment;
        contract.Definition.ItemName = "wf_contract_payment";
        db.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(608,1,42,99,'')");
        db.Connection.Execute("INSERT INTO wired_contracts(item_id,contract) VALUES(608,@Json)", new { Json = "{\"Payment\":[[{\"Amount\":1}]]}" });
        Assert.NotNull(db.Store.LoadContract(contract));
        var before = db.Connection.QuerySingle<string>("SELECT contract FROM wired_contracts WHERE item_id=608");
        await CanonicalHandler().Parse(_client, ClientPacket(608, 3, -1, 0, 0, "", 0, 0, 0, 0, 0, 0));
        Assert.Empty(store.Saves);
        Assert.Same(current, box.Configuration);
        Assert.Same(contract, _room.GetRoomItemHandler().GetItem(608));
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.WiredValidationErrorComposer);
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
        Assert.Equal(before, db.Connection.QuerySingle<string>("SELECT contract FROM wired_contracts WHERE item_id=608"));
    }

    private Item CanonicalPlace(uint id, int x, int y)
    {
        var item = Furni(id, InteractionType.None, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, x, y, 0, true, false, false));

        return item;
    }

    private static WiredNativeEditorConfiguration CanonicalNativeGroup(int movers, int targets) => new()
    {
        Category = WiredBoxCategory.Action,
        NativeCode = 57,
        OwnedIntParams = [0, 1, 0],
        Delay = 0,
        PrimaryItems = [new(602, false)],
        SecondaryItems = [new(603, false)],
        FurniSourceTypes = [movers, targets],
        UserSourceTypes = [0]
    };


    private (IWiredConfiguredItem Box, CanonicalStore Store) CanonicalBox(string name, WiredConfiguration previous)
    {
        var store = new CanonicalStore(previous);
        typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_room.GetWired(), store);
        var item = Furni(601, InteractionType.WiredEffect, WiredBoxType.None);
        item.RoomId = RoomId;
        item.Definition.InteractionName = name;
        item.Definition.ItemName = name;
        _room.GetRoomItemHandler().LoadFurniture([item]);
        Assert.True(_room.GetWired().TryGet(item.Id, out var loaded));
        var box = Assert.IsAssignableFrom<IWiredConfiguredItem>(loaded);
        _client.Packets.Clear();

        return (box, store);
    }

    private SaveWiredEffectConfigEvent CanonicalHandler() => new(CanonicalService((CanonicalStore)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetWired())!));

    private static WiredConfigurationService CanonicalService(CanonicalStore store) => new(store, null!, TestLogging.For<WiredConfigurationService>());

    private sealed class CanonicalStore(WiredConfiguration? previous) : IWiredConfigurationStore
    {
        public List<WiredConfiguration> Saves { get; } = [];
        public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor) => previous;
        public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration) => Saves.Add(configuration);
        public void Reset(IReadOnlyCollection<uint> itemIds) => throw new InvalidOperationException("Unexpected reset.");
    }
}
