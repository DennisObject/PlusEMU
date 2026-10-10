using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Badges;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Xunit;

namespace Plus.Tests;

[Collection("Modern Wired database seam")]
public sealed class WiredLegacyAccessDependencyTests
{

    private static Item Item(uint ownerId) => new()
    {
        Id = 77,
        UserId = checked((int)ownerId),
        OwnerId = ownerId,
        ExtraData = new LegacyDataFormat { Data = "0" },
        Definition = new ItemDefinition
        {
            ItemName = "wf_act_give_user_badge",
            WiredType = WiredBoxType.EffectGiveUserBadge,
            InteractionType = InteractionType.WiredEffect
        }
    };

    private static void Set(object value, string field, object data) =>
        value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
    private static object Get(object value, string field) =>
        value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
