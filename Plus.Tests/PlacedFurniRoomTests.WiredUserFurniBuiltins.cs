using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Data.Toner;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData("~pet.creation_time", 1609459200123L)]
    [InlineData("~pet.energy", 70L)]
    [InlineData("~pet.experience", 125L)]
    [InlineData("~pet.experience_required", 200L)]
    [InlineData("~pet.happiness", 80L)]
    [InlineData("~pet.level", 2L)]
    [InlineData("~pet.max_energy", 100L)]
    [InlineData("~pet.max_happiness", 150L)]
    [InlineData("~pet.max_level", 20L)]
    [InlineData("~pet.owner_id", 7L)]
    [InlineData("~pet.scratches", 3L)]
    [InlineData("@pet_owner_id", 7L)]
    public void NativePetVariablesReadActualPetStateAndExcludeMonsterplants(string key, long expected)
    {
        var pet = BuiltinPet(0);
        var holder = WiredVariableRuntimeFrames.UserHolder(pet);
        var frame = new WiredVariableFrame(RoomId, [holder]);
        var builtins = new RoomWiredBuiltinVariables(_room);
        var reference = new WiredVariableReference(holder.Target, "internal:" + key);
        Assert.Equal(expected, builtins.Read(reference, holder, frame)?.Value);
        Assert.True(RoomWiredBuiltinVariables.HasNumericValue(reference));
        pet.PetData.Type = 16;

        if (key.StartsWith("~pet.")) {
            Assert.Null(builtins.Read(reference, holder, frame));
        }

        Assert.Null(builtins.Read(reference, holder, new(RoomId, [])));
    }

    [Fact]
    public void NativeHorseFlagsArePresenceOnlyAndControllerUsesLiveRiderIdentity()
    {
        var horse = BuiltinPet(15);
        var rider = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        BuiltinUsers()[rider.VirtualId] = rider;
        var holder = WiredVariableRuntimeFrames.UserHolder(horse);
        var frame = new WiredVariableFrame(RoomId, [holder]);
        var builtins = new RoomWiredBuiltinVariables(_room);
        WiredVariableValue? Read(string key) => builtins.Read(new(holder.Target, "internal:" + key), holder, frame);
        Assert.Null(Read("~horse.has_saddle"));
        Assert.Null(Read("~horse.is_riding"));
        Assert.Equal(0, Read("~horse.controller_user_id")?.Value);
        horse.PetData.Saddle = 1;
        horse.RidingHorse = true;
        horse.HorseId = rider.VirtualId;
        Assert.Equal(1, Read("~horse.has_saddle")?.Value);
        Assert.Equal(1, Read("~horse.is_riding")?.Value);
        Assert.Equal(7, Read("~horse.controller_user_id")?.Value);
        Assert.False(RoomWiredBuiltinVariables.HasNumericValue(new(holder.Target, "internal:~horse.has_saddle")));
        Assert.False(RoomWiredBuiltinVariables.HasNumericValue(new(holder.Target, "internal:~horse.is_riding")));
        BuiltinUsers().TryRemove(rider.VirtualId, out _);
        Assert.Equal(0, Read("~horse.controller_user_id")?.Value);
        horse.PetData.Type = 0;
        Assert.Null(Read("~horse.controller_user_id"));
    }

    [Fact]
    public void PackedFurnitureAndBackgroundVariablesReadMatchingNativeObjects()
    {
        var item = Furni(200, InteractionType.None, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 1, 2, 4, true, false, false));
        var holder = WiredVariableRuntimeFrames.FurniHolder(item);
        var frame = new WiredVariableFrame(RoomId, [holder]);
        var builtins = new RoomWiredBuiltinVariables(_room);
        WiredVariableValue? Read(string key) => builtins.Read(new(holder.Target, "internal:" + key), holder, frame);
        Assert.Equal(258, Read("@position")?.Value);
        Assert.Equal(66052, Read("@occupation")?.Value);
        item.Definition.InteractionType = InteractionType.Toner;
        _room.TonerData = new(item.Id, new TonerRecord { Hue = 22, Saturation = 33, Lightness = 44 });
        Assert.Equal(22, Read("~background_color.hue")?.Value);
        Assert.Equal(33, Read("~background_color.saturation")?.Value);
        Assert.Equal(44, Read("~background_color.lightness")?.Value);
        _room.TonerData.ItemId++;
        Assert.Null(Read("~background_color.hue"));
    }

    [Fact]
    public void TonerWiredWritesPersistBeforePublishingAndRefuseInvalidDomainsOrFailedPersistence()
    {
        var item = Furni(201, InteractionType.None, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 1, 1, 0, true, false, false));
        item.Definition.InteractionType = InteractionType.Toner;
        _room.TonerData = new(item.Id, new TonerRecord { Hue = 22, Saturation = 33, Lightness = 44 });
        var calls = 0;
        var fail = false;
        var store = Proxy<IRoomItemMetadataStore>((method, args) =>
        {
            Assert.Equal("SetToner", method);
            Assert.Equal((item.Id, RoomId, fail ? 0 : 255, 33, 44), ((uint)args[0]!, (uint)args[1]!, (int)args[2]!, (int)args[3]!, (int)args[4]!));
            Assert.Equal(fail ? 255 : 22, _room.TonerData.Hue);
            calls++;

            if (fail) {
                throw new InvalidOperationException("isolated persistence failure");
            }

            return null;
        });
        var holder = WiredVariableRuntimeFrames.FurniHolder(item);
        var frame = new WiredVariableFrame(RoomId, [holder]);
        var module = new WiredVariableModule(RoomId, new BuiltinDefinitionDirectory(), new MemoryWiredVariableStore(), _interactionClock,
            new RoomWiredBuiltinVariables(_room, metadataStore: store));
        var reference = new WiredVariableReference(holder.Target, "internal:~background_color.hue");
        Assert.False(module.Mutate(reference, holder, WiredVariableMutation.Set, -1, frame));
        Assert.False(module.Mutate(reference, holder, WiredVariableMutation.Set, 256, frame));
        Assert.False(module.Mutate(reference, holder, WiredVariableMutation.Set, 2147483648L, frame));
        Assert.Equal(0, calls);
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Set, 255, frame));
        Assert.Equal((255, 33, 44, 1), (_room.TonerData.Hue, _room.TonerData.Saturation, _room.TonerData.Lightness, _room.TonerData.Enabled));
        var change = Assert.Single(module.DrainChanges());
        Assert.Equal((22L, 255L), (change.Before!.Value, change.After!.Value));
        Assert.Same(item, ((ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_movedItems", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_room.GetRoomItemHandler())!)[item.Id]);
        fail = true;
        Assert.Throws<InvalidOperationException>(() => module.Mutate(reference, holder, WiredVariableMutation.Set, 0, frame));
        Assert.Equal(255, _room.TonerData.Hue);
        Assert.Equal(2, calls);
        Assert.Empty(module.DrainChanges());
    }

    [Fact]
    public void CreatorMenuAcceptsAnUnchangedCustomValueAndStillRejectsUnauthorizedDefinitions()
    {
        var variables = _room.GetWired().Variables;
        var module = new WiredVariableModule(RoomId, new BuiltinDefinitionDirectory(), new MemoryWiredVariableStore(), _interactionClock);
        typeof(WiredRoomVariables).GetField("<Module>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(variables, module);
        var menu = new WiredVariableMenu(_room, variables);
        Assert.True(menu.Write(WiredVariableTarget.Global, 0, 991, long.MaxValue, WiredVariableMutation.Set));
        module.DrainChanges();
        Assert.True(menu.Write(WiredVariableTarget.Global, 0, 991, long.MaxValue, WiredVariableMutation.Set));
        Assert.Equal(2, Assert.Single(module.DrainChanges()).Origin);
        Assert.False(menu.Write(WiredVariableTarget.Global, 0, 992, long.MaxValue, WiredVariableMutation.Set));
        Assert.Empty(module.DrainChanges());
    }

    [Fact]
    public void UserPackedPositionFavouriteGroupAndGroupAdminUseActualMembership()
    {
        var user = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 1, Y = 2 };
        BuiltinUsers()[user.VirtualId] = user;
        _client.GetHabbo().HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
        var holder = WiredVariableRuntimeFrames.UserHolder(user);
        var frame = new WiredVariableFrame(RoomId, [holder]);
        var builtins = new RoomWiredBuiltinVariables(_room);
        WiredVariableValue? Read(string key) => builtins.Read(new(holder.Target, "internal:" + key), holder, frame);
        Assert.Equal(258, Read("@position")?.Value);
        Assert.Null(Read("@favourite_group_id"));
        _client.GetHabbo().HabboStats.FavouriteGroupId = 20;
        Assert.Equal(20, Read("@favourite_group_id")?.Value);
        Assert.Null(Read("@is_group_admin"));
        _room.Group = new(20, "group", "", "", RoomId, 9, null, 0, 1, 1, 0, false,
            new Plus.HabboHotel.Groups.GroupMembershipSnapshot([], [7], []));
        Assert.Equal(1, Read("@is_group_admin")?.Value);
        _room.Group.TakeAdmin(7);
        Assert.Null(Read("@is_group_admin"));
        _room.Group.CreatorId = 7;
        Assert.Equal(1, Read("@is_group_admin")?.Value);
        Assert.False(RoomWiredBuiltinVariables.HasNumericValue(new(holder.Target, "internal:@is_group_admin")));
    }

    [Fact]
    public void TeleportTargetWritesOnlyTheValidatedSourceHalfAndPreserveSourcePermittedDanglingTargets()
    {
        var source = Furni(202, InteractionType.None, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, source, 1, 1, 0, true, false, false));
        source.Definition.InteractionType = InteractionType.Teleport;
        var target = 300u;
        var writes = 0;
        var travel = Proxy<IItemTravelStore>((method, args) =>
        {
            Assert.Equal(source.Id, (uint)args[0]!);

            if (method == "FindLinkedTeleporter") {
                return target;
            }

            Assert.Equal("SetLinkedTeleporter", method);
            Assert.Equal(RoomId, (uint)args[1]!);
            target = (uint)args[2]!;
            writes++;

            return true;
        });
        var holder = WiredVariableRuntimeFrames.FurniHolder(source);
        var frame = new WiredVariableFrame(RoomId, [holder]);
        var builtin = new RoomWiredBuiltinVariables(_room, travelStore: travel);
        var module = new WiredVariableModule(RoomId, new BuiltinDefinitionDirectory(), new MemoryWiredVariableStore(), _interactionClock, builtin);
        var reference = new WiredVariableReference(holder.Target, "internal:~teleport.target_id");
        Assert.Equal(300, module.Read(reference, holder, frame)?.Value);
        Assert.False(module.Mutate(reference, holder, WiredVariableMutation.Set, 0, frame));
        Assert.False(module.Mutate(reference, holder, WiredVariableMutation.Set, -1, frame));
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Set, 400, frame));
        Assert.Equal(400, module.Read(reference, holder, frame)?.Value);
        Assert.Equal(1, writes);
        Assert.False(builtin.Write(reference, holder, 500, new(RoomId, [])));
        source.Definition.InteractionType = InteractionType.None;
        Assert.Null(module.Read(reference, holder, frame));
        Assert.Equal(1, writes);
    }

    [Fact]
    public void OfficialProjectileSpellingsReadTheExistingFlightAndKeepItsPresenceSemantics()
    {
        var item = Furni(203, InteractionType.None, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 1, 1, 0, true, false, false));
        var engine = (WiredStackEngine)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_room.GetWired())!;
        var flights = Plus.HabboHotel.Items.Wired.Modern.Actions.WiredProjectileFlights.For(_room);
        Assert.True(flights.Begin(item, 0, 0, 0, 100000, engine.NowMilliseconds));
        var holder = WiredVariableRuntimeFrames.FurniHolder(item);
        var frame = new WiredVariableFrame(RoomId, [holder]);
        var builtins = new RoomWiredBuiltinVariables(_room, engineRead: _room.GetWired().ReadBuiltin);
        WiredVariableValue? Read(string key) => builtins.Read(new(holder.Target, "internal:" + key), holder, frame);
        Assert.Equal(1, Read("@projectile.animation.is_travelling")?.Value);
        Assert.Equal(Read("@projectile.animation.tiles_traveled")?.Value, Read("@projectile.animation.tiles_travelled")?.Value);
        Assert.True(RoomWiredBuiltinVariables.HasNumericValue(new(holder.Target, "internal:@projectile.animation.tiles_travelled")));
        Assert.False(RoomWiredBuiltinVariables.HasNumericValue(new(holder.Target, "internal:@projectile.animation.is_travelling")));
        flights.Forget(item);
        Assert.Null(Read("@projectile.animation.is_travelling"));
    }

    [Fact]
    public void TeamTypeUsesKnownJoinAndNativeMembershipAndNeverGuessesFromColor()
    {
        var user = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused);
        BuiltinUsers()[user.VirtualId] = user;
        _client.GetHabbo().Effects = new Plus.HabboHotel.Users.Effects.EffectsComponent(_interactionClock);
        var holder = WiredVariableRuntimeFrames.UserHolder(user);
        var frame = new WiredVariableFrame(RoomId, [holder]);
        var builtins = new RoomWiredBuiltinVariables(_room);
        WiredVariableValue? Read() => builtins.Read(new(holder.Target, "internal:@team.type"), holder, frame);
        var state = Plus.HabboHotel.Items.Wired.Modern.Actions.WiredGameState.For(_room);
        user.Team = Plus.HabboHotel.Rooms.Games.Teams.Team.Red;
        Assert.Null(Read());
        user.Team = Plus.HabboHotel.Rooms.Games.Teams.Team.None;
        Assert.True(state.Join(_room, user, 0, Plus.HabboHotel.Rooms.Games.Teams.Team.Red, 0, [user]));
        Assert.Equal(4, Read()?.Value);
        user.Team = Plus.HabboHotel.Rooms.Games.Teams.Team.Blue;
        Assert.Null(Read()); // An external color change does not retain stale Wired provenance.
        user.Team = Plus.HabboHotel.Rooms.Games.Teams.Team.Red;
        // A native gate can change membership without going through the Wired Join adapter.
        _room.GetTeamManagerForBanzai().RedTeam.Add(user);
        Assert.Equal(0, Read()?.Value);
        _room.GetTeamManagerForBanzai().RedTeam.Clear();
        _room.GetTeamManagerForFreeze().RedTeam.Add(user);
        Assert.Equal(1, Read()?.Value);
        Assert.True(state.Leave(_room, user));
        Assert.Null(Read());
        user.Team = Plus.HabboHotel.Rooms.Games.Teams.Team.Blue;
        Assert.Null(Read());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void VariableTimestampSortExcludesNativeIdsWithoutTimestampsAndKeepsStableCustomTies(int sort)
    {
        var items = Enumerable.Range(200, 3).Select(id => Furni((uint)id, InteractionType.None, WiredBoxType.None)).ToArray();

        for (var index = 0; index < items.Length; index++) {
            Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, items[index], 1, index + 1, 0, true, false, false));
        }

        var holders = new[] { items[2], items[0], items[1] }.Select(WiredVariableRuntimeFrames.FurniHolder).ToArray();
        var frame = new WiredVariableFrame(RoomId, holders);
        var module = new WiredVariableModule(RoomId, new SortDefinitionDirectory(RoomId), new MemoryWiredVariableStore(),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch), new RoomWiredBuiltinVariables(_room));
        var native = WiredVariablePredicates.Filter(module, new(WiredVariableTarget.Furni, "internal:@id"), holders, frame, sort, 10);

        if (sort < 2) {
            Assert.Equal(sort == 0 ? holders.OrderByDescending(holder => holder.EntityId) : holders.OrderBy(holder => holder.EntityId), native);
        }
        else {
            Assert.Empty(native);
        }

        foreach (var holder in holders) {
            Assert.True(module.Mutate(new(holder.Target, "custom:991"), holder, WiredVariableMutation.Give, 7, frame));
        }

        Assert.Equal(holders, WiredVariablePredicates.Filter(module, new(WiredVariableTarget.Furni, "custom:991"), holders, frame, sort, 10));
    }

    private sealed class SortDefinitionDirectory(uint roomId) : IWiredVariableDirectory
    {
        public uint? GetRoomOwner(uint id) => 7;
        public WiredVariableDefinition? Find(uint id) => id == 991 ? new(id, roomId, 7, "ties", WiredVariableTarget.Furni, WiredVariableAvailability.RoomActive, true) : null;
    }

    private sealed class BuiltinDefinitionDirectory : IWiredVariableDirectory
    {
        public uint? GetRoomOwner(uint roomId) => 7;
        public WiredVariableDefinition? Find(uint id) => id == 991 ? new(id, RoomId, 7, "wide", WiredVariableTarget.Global, WiredVariableAvailability.RoomActive, true) : null;
    }

    private ConcurrentDictionary<int, RoomUser> BuiltinUsers() => (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
        .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;

    private RoomUser BuiltinPet(int type)
    {
        var bot = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
        bot.AiType = BotAiType.Pet;
        var avatar = new RoomUser(0, RoomId, 8, _room, null, TestChatEmotions.Unused, TestRewardProgress.Unused)
        {
            BotData = bot,
            InternalRoomId = 8,
            PetData = new Pet(80, 7, RoomId, "test", type, "0", "ffffff", 125, 70, 80, 3,
                DateTimeOffset.FromUnixTimeMilliseconds(1609459200123), 1, 2, 0, 0, 0, 0, 0, "", "owner")
        };
        BuiltinUsers()[avatar.VirtualId] = avatar;

        return avatar;
    }
}
