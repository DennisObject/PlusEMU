using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Pets;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Xunit;

namespace Plus.Tests;

public sealed class RemovalAndRespectWireTests
{
    [Theory]
    [InlineData(7u, false, "7")]
    [InlineData(uint.MaxValue, false, "4294967295")]
    [InlineData(uint.MaxValue, true, "-1")]
    public void FloorRemovalRetainsCapturedIdentityAndTemporaryEncoding(uint id, bool temporary, string wireId)
    {
        var item = new Item { Id = id, IsTemporary = temporary };
        var composer = new ObjectRemoveComposer(item.Id, item.IsTemporary, 42);
        item.Id = 8;
        Recompose(composer, [wireId, false, 42, 0]);
    }

    [Fact]
    public void WallAndControllerRemovalRetainCapturedIdentifiers()
    {
        var item = new Item { Id = uint.MaxValue };
        var wall = new ItemRemoveComposer(item.Id, 42);
        item.Id = 8;
        Recompose(wall, ["4294967295", false, 42]);
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 9;
        var controller = new FlatControllerRemovedComposer(room.Id, 42);
        room.Id = 10;
        Recompose(controller, [9u, 42]);
    }

    [Fact]
    public void PetAndHumanRespectRetainCapturedFieldsAfterSourceMutation()
    {
        var pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        pet.VirtualId = 11;
        pet.PetId = 12;
        pet.Name = "Pet";
        pet.Color = "AA00BB";
        var composer = new RespectPetNotificationComposer(pet.VirtualId, pet.PetId, pet.Name, pet.Color);
        pet.VirtualId = 21;
        pet.PetId = 22;
        pet.Name = "changed";
        pet.Color = "FFFFFF";
        Recompose(composer, [11, 11, 12, "Pet", 0, 0, "AA00BB", 0, 0, 1]);
        Recompose(new RespectPetNotificationComposer(31, 32, "Human", "FFFFFF"),
            [31, 31, 32, "Human", 0, 0, "FFFFFF", 0, 0, 1]);
    }

    private static void Recompose(IServerPacket composer, object[] expected)
    {
        Assert.All(composer.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => Assert.True(field.FieldType.IsPrimitive || field.FieldType == typeof(string)));

        for (var index = 0; index < 2; index++)
        {
            var output = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(output);
            Assert.Equal(expected, output.Writes);
        }
    }
}
