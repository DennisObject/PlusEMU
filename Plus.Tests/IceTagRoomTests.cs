using System.Reflection;
using Plus.HabboHotel;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Permissions;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(false, "M", 45, 38)]
    [InlineData(false, "F", 46, 39)]
    [InlineData(true, "M", 45, 38)]
    [InlineData(true, "F", 46, 39)]
    public void IceTagTransfersOnlyToAdjacentAdmittedSkatersAndPulsesPole(bool v2, string gender, int tagged, int skating)
    {
        var awards = IceTagSetup(v2);
        var field = IceField(91, 1, 1);
        var pole = IcePole(92, 0, 3);
        var source = IcePlayer(_client, 1, 1, gender);
        var targetClient = IceClient(8, gender);
        var target = IcePlayer(targetClient, 2, 1, gender);
        var actions = new RoomAvatarActionService(_interactionClock, Proxy<Plus.HabboHotel.Quests.IQuestManager>((method, _) => throw new InvalidOperationException(method)), TestNavigationRewards.Instance);
        IceContact(source, v2);
        IceContact(target, v2);
        Assert.Equal(tagged, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        Assert.Equal(skating, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(targetClient.GetHabbo().Effects).CurrentEffect);

        actions.LookTo(_room, _client, int.MinValue, int.MaxValue);
        Assert.Equal(tagged, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        actions.LookTo(_room, _client, 3, 3);
        Assert.Equal(tagged, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        actions.LookTo(_room, _client, 2, 1);
        Assert.Equal(skating, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        Assert.Equal(tagged, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(targetClient.GetHabbo().Effects).CurrentEffect);
        Assert.Equal("1", pole.LegacyDataString);
        IceContact(source, v2);
        IceContact(target, v2);
        Assert.Equal(tagged, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(targetClient.GetHabbo().Effects).CurrentEffect);

        _interactionClock.Now = _interactionClock.Now.AddMilliseconds(999);
        _room.ProcessWiredOnly();
        Assert.Equal("1", pole.LegacyDataString);
        _interactionClock.Now = _interactionClock.Now.AddMilliseconds(1);
        _room.ProcessWiredOnly();
        Assert.Equal("0", pole.LegacyDataString);
        Assert.Equal([(7, "ACH_TagA", 1)], awards);
        Assert.Same(field, _room.GetRoomItemHandler().GetItem(91));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IceTagWalkUsesTheRealMovementEngineForEntryAndExit(bool v2)
    {
        var awards = IceTagSetup(v2);
        IceField(91, 1, 1);
        IcePole(92, 0, 3);
        var actor = IcePlayer(_client, 0, 1);
        var actions = new RoomAvatarActionService(_interactionClock, Proxy<Plus.HabboHotel.Quests.IQuestManager>((method, _) => throw new InvalidOperationException(method)), TestNavigationRewards.Instance);
        actions.Move(_client, 1, 1);

        for (var i = 0; i < 12; i++) {
            _room.ProcessRoom();
        }

        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Equal(45, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        _interactionClock.Now = _interactionClock.Now.AddMinutes(1);
        actions.Move(_client, 0, 1);

        for (var i = 0; i < 12; i++) {
            _room.ProcessRoom();
        }

        Assert.Equal((0, 1), (actor.X, actor.Y));
        Assert.Equal(-1, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        Assert.Contains((7, "ACH_TagC", 1), awards);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IceTagLimitsRolesToPolesAndClearsRemovedPoleAndField(bool v2)
    {
        IceTagSetup(v2);
        var field = IceField(91, 1, 1);
        var pole = IcePole(92, 0, 3);
        IcePole(93, 3, 0);
        var a = IcePlayer(_client, 1, 1);
        var bClient = IceClient(8);
        var b = IcePlayer(bClient, 2, 1);
        var cClient = IceClient(9);
        var c = IcePlayer(cClient, 1, 2);
        IceContact(a, v2);
        IceContact(b, v2);
        IceContact(c, v2);
        Assert.Equal(45, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        Assert.Equal(45, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(bClient.GetHabbo().Effects).CurrentEffect);
        Assert.Equal(38, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(cClient.GetHabbo().Effects).CurrentEffect);
        _room.GetRoomItemHandler().RemoveFurniture(_client, pole.Id);
        Assert.Equal(38, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        Assert.Equal(45, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(bClient.GetHabbo().Effects).CurrentEffect);
        Assert.Equal("0", pole.LegacyDataString);
        _room.GetRoomItemHandler().RemoveFurniture(_client, field.Id);
        Assert.Equal(-1, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(bClient.GetHabbo().Effects).CurrentEffect);
        Assert.Equal(-1, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(cClient.GetHabbo().Effects).CurrentEffect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IceTagAwardsWholeMinutesPerFieldVisitAndNotAgainAfterDeparture(bool v2)
    {
        var awards = IceTagSetup(v2);
        IceField(91, 1, 1);
        var actor = IcePlayer(_client, 1, 1);
        IceContact(actor, v2);
        _interactionClock.Now = _interactionClock.Now.AddSeconds(119);
        IceContact(actor, v2);
        Assert.DoesNotContain(awards, award => award.Item2 == "ACH_TagC");
        actor.SetPos(0, 1, 0);
        IceContact(actor, v2);
        Assert.Contains((7, "ACH_TagC", 1), awards);
        Assert.Equal(-1, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        actor.SetPos(1, 1, 0);
        IceContact(actor, v2);
        _interactionClock.Now = _interactionClock.Now.AddSeconds(60);
        _room.GetRoomUserManager().RemoveUserFromRoom(_client, false);
        Assert.Equal(2, awards.Count(award => award.Item2 == "ACH_TagC"));
        Assert.Null(_client.GetHabbo().CurrentRoom);
        Assert.Equal(-1, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        _room.GetIceTag().Leave(actor);
        Assert.Equal(2, awards.Count(award => award.Item2 == "ACH_TagC"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IceTagRejectsStaleActorAndForeignFieldAndKeepsNextRoomEffect(bool v2)
    {
        IceTagSetup(v2);
        var field = IceField(91, 1, 1);
        IcePole(92, 0, 3);
        var source = IcePlayer(_client, 1, 1);
        var targetClient = IceClient(8);
        var target = IcePlayer(targetClient, 2, 1);
        IceContact(source, v2);
        IceContact(target, v2);
        var stale = new RoomUser(7, RoomId, source.VirtualId, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 1, Y = 1 };
        Assert.Equal(-1, _room.GetIceTag().Update(stale, field));
        _room.GetIceTag().LookTo(stale, 2, 1);
        Assert.Equal(38, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(targetClient.GetHabbo().Effects).CurrentEffect);
        var foreign = Furni(91, InteractionType.IceSkates, WiredBoxType.None);
        Assert.Equal(-1, _room.GetIceTag().Update(source, foreign));
        Assert.Equal(45, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        var nextRoom = new Room(new() { Id = RoomId }, [], TestLogging.Navigation, TestLogging.Logger, TestRoomAchievements.Unused, TestRoomOwners.Unused);
        _client.GetHabbo().CurrentRoom = nextRoom;
        Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect = 77;
        _room.GetIceTag().Leave(source);
        Assert.Equal(77, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        Assert.Equal(-1, _room.GetIceTag().Update(source, field));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IceTagFieldPlacementAwardsOnceAndKeepsOtherFloorEffects(bool v2)
    {
        var awards = IceTagSetup(v2);
        var field = IceField(91, 1, 1);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(_client, field, 0, 1, 0, false, false, false));
        Assert.Single(awards);
        var actor = IcePlayer(_client, 1, 1);
        IceContact(actor, v2);
        var pool = Furni(92, InteractionType.Pool, WiredBoxType.None);
        pool.Definition.Walkable = true;
        pool.Definition.Height = 0;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, pool, 3, 1, 0, true, false, false));
        actor.SetPos(3, 1, 0);
        IceContact(actor, v2);
        Assert.Equal(29, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        Assert.Equal(ItemEffectType.Swim, actor.CurrentItemEffect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IceTagPoleResetCannotWriteToAnEarlierPlacementOrReplacement(bool v2)
    {
        IceTagSetup(v2);
        IceField(91, 1, 1);
        var old = IcePole(92, 0, 3);
        var a = IcePlayer(_client, 1, 1);
        var b = IcePlayer(IceClient(8), 2, 1);
        IceContact(a, v2);
        IceContact(b, v2);
        var actions = new RoomAvatarActionService(_interactionClock, Proxy<Plus.HabboHotel.Quests.IQuestManager>((method, _) => throw new InvalidOperationException(method)), TestNavigationRewards.Instance);
        actions.LookTo(_room, _client, 2, 1);
        Assert.Equal("1", old.LegacyDataString);
        _room.GetRoomItemHandler().RemoveFurniture(_client, old.Id);
        var replacement = IcePole(92, 0, 3);
        replacement.LegacyDataString = "1";
        _interactionClock.Now = _interactionClock.Now.AddSeconds(2);
        _room.ProcessWiredOnly();
        Assert.Equal("1", replacement.LegacyDataString);
        Assert.Null(old.GetRoom());
        Assert.Same(replacement, _room.GetRoomItemHandler().GetItem(92));
    }

    [Theory]
    [InlineData(false, "M", 38, 45)]
    [InlineData(false, "F", 39, 46)]
    [InlineData(true, "M", 38, 45)]
    [InlineData(true, "F", 39, 46)]
    public void IceTagWithoutPoleKeepsOrdinarySkatingAndRoleFollowsPoleLifetime(bool v2, string gender, int skating, int tagged)
    {
        var awards = IceTagSetup(v2);
        IceField(91, 1, 1);
        var actor = IcePlayer(_client, 1, 1, gender);
        IceContact(actor, v2);
        IceContact(actor, v2);
        Assert.Equal(skating, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        var pole = IcePole(92, 0, 3);
        IceContact(actor, v2);
        Assert.Equal(tagged, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        _room.GetRoomItemHandler().RemoveFurniture(_client, pole.Id);
        Assert.Equal(skating, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        IceContact(actor, v2);
        IceContact(actor, v2);
        Assert.Equal(skating, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
        _interactionClock.Now = _interactionClock.Now.AddMinutes(1);
        actor.SetPos(0, 1, 0);
        IceContact(actor, v2);
        Assert.Contains((7, "ACH_TagC", 1), awards);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IceTagEffectSendCanRemoveSourceAndMoveTargetWithoutTouchingNextRoomEffect(bool v2)
    {
        IceTagSetup(v2);
        IceField(91, 1, 1);
        IcePole(92, 0, 3);
        var source = IcePlayer(_client, 1, 1);
        var targetClient = IceClient(8);
        var target = IcePlayer(targetClient, 2, 1);
        IceContact(source, v2);
        IceContact(target, v2);
        var moved = false;
        _client.BeforeCapture = header =>
        {
            if (!moved && header == Plus.Communication.Packets.Outgoing.ServerPacketHeader.AvatarEffectComposer) {
                moved = true;
                targetClient.GetHabbo().CurrentRoom = new Room(new() { Id = 99 }, [], TestLogging.Navigation, TestLogging.Logger, TestRoomAchievements.Unused, TestRoomOwners.Unused);
                Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(targetClient.GetHabbo().Effects).CurrentEffect = 77;
                _room.GetRoomUserManager().RemoveUserFromRoom(_client, false);
            }
        };
        var actions = new RoomAvatarActionService(_interactionClock, Proxy<Plus.HabboHotel.Quests.IQuestManager>((method, _) => throw new InvalidOperationException(method)), TestNavigationRewards.Instance);
        actions.LookTo(_room, _client, 2, 1);
        Assert.True(moved);
        Assert.Null(_client.GetHabbo().CurrentRoom);
        Assert.Equal(77, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(targetClient.GetHabbo().Effects).CurrentEffect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IceTagMinuteAwardCannotOverwriteAReplacementRoomsEffect(bool v2)
    {
        IceTagSetup(v2);
        var awarded = false;
        Set("_achievements", new TestRoomAchievements((client, group, amount) =>
        {
            if (group == "ACH_TagA") {
                return;
            }

            Assert.Equal("ACH_TagC", group);
            Assert.Equal(1, amount);
            awarded = true;
            client.GetHabbo().CurrentRoom = new Room(new() { Id = 99 }, [], TestLogging.Navigation, TestLogging.Logger, TestRoomAchievements.Unused, TestRoomOwners.Unused);
            Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(client.GetHabbo().Effects).CurrentEffect = 77;
        }));
        IceField(91, 1, 1);
        var actor = IcePlayer(_client, 1, 1);
        IceContact(actor, v2);
        _interactionClock.Now = _interactionClock.Now.AddMinutes(1);
        actor.SetPos(0, 1, 0);
        IceContact(actor, v2);
        Assert.True(awarded);
        Assert.Equal(77, Assert.IsType<Plus.HabboHotel.Users.Effects.EffectsComponent>(_client.GetHabbo().Effects).CurrentEffect);
    }

    [Fact]
    public void IceTagStorageAliasesAndPoleInteractorDoNotToggleManually()
    {
        Assert.Equal(InteractionType.IceSkates, InteractionTypes.GetTypeFromString("iceskates"));
        Assert.Equal(InteractionType.IceSkates, InteractionTypes.GetTypeFromString("icetag_field"));
        Assert.Equal(InteractionType.IceTagPole, InteractionTypes.GetTypeFromString("icetag_pole"));
        IceTagSetup(false);
        var pole = IcePole(92, 0, 3);
        pole.Interactor.OnTrigger(_client, pole, 1, true);
        Assert.Equal("0", pole.LegacyDataString);
    }

    private List<(int, string, int)> IceTagSetup(bool v2)
    {
        var awards = new List<(int, string, int)>();
        Set("_achievements", new TestRoomAchievements((client, group, amount) => awards.Add((client.GetHabbo().Id, group, amount))));
        _room.WordFilterList = [];

        if (v2) {
            var map = _room.GetGameMap();
            var navigation = new RoomNavigation(_room, map.StaticModel, new() { Engine = PathfindingEngine.V2 }, TestLogging.Navigation,
                TestGroupManager.Empty, _database, TestNavigationRewards.Instance);
            typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(map, navigation);
        }

        _gameField.SetValue(null, Proxy<IGame>((method, _) => throw new InvalidOperationException(method)));

        return awards;
    }

    private TestClient IceClient(int id, string gender = "M")
    {
        var client = new TestClient();
        client.SetHabbo(new Habbo { Id = id, Username = $"skater{id}", CurrentRoom = _room, Access = UserAccess.Empty, Gender = gender });

        return client;
    }

    private RoomUser IcePlayer(TestClient client, int x, int y, string gender = "M")
    {
        var habbo = client.GetHabbo();
        habbo.Gender = gender;
        habbo.Client = client;
        habbo.Effects = new(_interactionClock);
        habbo.Effects.Init(habbo);
        habbo.IgnoresComponent = new([]);
        Assert.True(_room.GetRoomUserManager().AddAvatarToRoom(client));
        var actor = Assert.IsType<RoomUser>(_room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id));
        actor.SetPos(x, y, 0);

        if (_room.UsesV2Movement) {
            _room.ProcessRoom();
        }

        return actor;
    }

    private Item IceField(uint id, int x, int y)
    {
        var item = Furni(id, InteractionType.IceSkates, WiredBoxType.None);
        item.Definition.ItemName = "es_skating_ice";
        item.Definition.Walkable = true;
        item.Definition.Width = item.Definition.Length = 2;
        item.Definition.Height = 0.01;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(_client, item, x, y, 0, true, false, false));

        return item;
    }

    private Item IcePole(uint id, int x, int y)
    {
        var item = Furni(id, InteractionType.IceTagPole, WiredBoxType.None);
        item.ExtraData = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat { Data = "0" };
        item.Definition.ItemName = "es_tagging";
        item.Definition.Walkable = false;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(_client, item, x, y, 0, true, false, false));

        return item;
    }

    private void IceContact(RoomUser actor, bool v2)
    {
        if (v2) {
            _room.ProcessRoom();
            new FloorEffectService(_room, _ => { }).Apply(actor, actor.X, actor.Y);
        }
        else {
            _room.GetRoomUserManager().OnCycle();
        }
    }
}
