using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredExecutionLimitTests
{
    [Fact]
    public void FailedConditionConsumesTheExecutionLimitBeforeAnotherAttempt()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var furniture = new List<Item>();
        var errors = new List<Exception>();
        long now = 0;
        var engine = new WiredStackEngine(() => now, box => furniture.Contains(box.Item), _ => true, _ => { }, errors.Add);
        engine.BindRuntime(room, new(() => furniture, () => [],
            id => furniture.FirstOrDefault(x => x.Id == id), _ => null), new Operations());
        var trigger = Add(engine, furniture, room, new Trigger());
        var condition = Add(engine, furniture, room, new Condition());
        var action = Add(engine, furniture, room, new Effect());
        var limitItem = Furni(furniture, "wf_xtra_execution_limit");
        var limit = WiredAddonFactory.Create(room, limitItem, new(), TestGroupManager.Empty, readWorld: _ => new(1, 1, [], []))!;
        Assert.True(limit.TryValidateConfiguration(new() { IntParams = [1, 2] }, out var valid, out var error), error);
        limit.ApplyConfiguration(valid);
        Assert.False(limit.AfterConditions);
        Assert.Equal(1000, limit.Configuration.IntParams[1]);
        Assert.True(engine.Add(limit));

        condition.Passes = false;
        Assert.False(engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Equal(1, condition.Calls);
        Assert.Equal(0, action.Calls);
        Assert.Equal(1, trigger.Calls);

        condition.Passes = true;
        now = 500;
        Assert.False(engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Equal(1, condition.Calls);
        Assert.Equal(0, action.Calls);

        now = 1000;
        Assert.True(engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter)));
        Assert.Equal(2, condition.Calls);
        Assert.Equal(0, action.Calls);
        engine.OnFastCycle();
        Assert.Equal(1, action.Calls);
        Assert.Empty(errors);
    }

    private static T Add<T>(WiredStackEngine engine, List<Item> furniture, Room room, T box) where T : Box
    {
        box.Instance = room;
        box.Item = Furni(furniture);
        Assert.True(engine.Add(box));

        return box;
    }

    private static Item Furni(List<Item> furniture, string interaction = "")
    {
        var item = new Item
        {
            Id = (uint)furniture.Count + 1,
            GetZ = furniture.Count + 1,
            Definition = new()
            {
                InteractionName = interaction,
                ItemName = "test",
                PublicName = "test",
                VendingIds = [],
                AdjustableHeights = []
            }
        };
        furniture.Add(item);

        return item;
    }

    private sealed class Operations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => false;
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => false;
        public void ResetTimers(IEnumerable<Item> targets) { }
    }

    private class Box(WiredBoxCategory category) : IWiredContextualItem
    {
        public Room Instance { get; set; } = null!;
        public Item Item { get; set; } = null!;
        public WiredBoxType Type => WiredBoxType.None;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public WiredBoxDescriptor Descriptor { get; } = new("test", category, 0, "test") { Support = WiredBoxSupport.Implemented };
        public WiredConfiguration Configuration { get; private set; } = new();
        public int Calls;
        public virtual bool Execute(WiredRuntimeContext context)
        {
            Calls++;

            return true;
        }
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments) => throw new InvalidOperationException("Modern box requires context");
        public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        {
            validated = proposed;
            error = "";

            return true;
        }
        public void ApplyConfiguration(WiredConfiguration validated) => Configuration = validated;
    }

    private sealed class Trigger() : Box(WiredBoxCategory.Trigger), IWiredContextualTrigger
    {
        public IReadOnlyCollection<WiredEventKind> Events { get; } = [WiredEventKind.Enter];
        public bool HidesChat(WiredRuntimeContext context) => false;
    }

    private sealed class Condition() : Box(WiredBoxCategory.Condition)
    {
        public bool Passes;
        public override bool Execute(WiredRuntimeContext context)
        {
            Calls++;

            return Passes;
        }
    }

    private sealed class Effect() : Box(WiredBoxCategory.Action), IWiredContextualAction
    {
        public bool IsNegative => false;
    }
}
