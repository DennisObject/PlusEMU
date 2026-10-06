using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Outgoing.Inventory.Pets;
using Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class PetWireTests
{
    [Fact]
    public void InventoryUsesZeroBasedFragmentAndRealPetLevel()
    {
        var pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        pet.PetId = 42;
        pet.Name = "LifePet";
        pet.Type = 12;
        pet.Race = "2";
        pet.Color = "FFFFFF";
        pet.PetHair = -1;
        pet.GnomeClothing = "-1";
        pet.Experience = 300;
        pet.ExperienceLevels = [100, 200, 400];
        var packet = new RecordingPacket();

        var pets = new List<Pet> { pet };
        var composer = new PetInventoryComposer(PetAppearanceSnapshots.Inventory(pets));
        composer.Compose(packet);

        Assert.Equal(new object[] { 1, 0, 1, 42, "LifePet", 12, 2, "FFFFFF", 0, 2, 2, -1, 0, 3, -1, 0, 3 }, packet.Writes);
        pets.Clear();
        pet.Name = "Changed";
        pet.Race = "invalid";
        pet.Experience = 0;
        var repeated = new RecordingPacket();
        composer.Compose(repeated);
        Assert.Equal(packet.Writes, repeated.Writes);
    }

    [Theory]
    [InlineData(0, "-1", "12 2 FFFFFF 2 2 -1 0 3 -1 0")]
    [InlineData(9, "-1", "12 2 FFFFFF 3 2 -1 0 3 -1 0 4 9 0")]
    [InlineData(0, "1 3 302 4", "12 2 FFFFFF 1 3 302 4")]
    public void RoomAndInventoryUseTheSameCompleteCustomParts(int saddle, string clothing, string expectedFigure)
    {
        var pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        pet.PetId = 42;
        pet.Name = "LifePet";
        pet.Type = 12;
        pet.Race = "2";
        pet.Color = "FFFFFF";
        pet.PetHair = -1;
        pet.Saddle = saddle;
        pet.GnomeClothing = clothing;
        pet.ExperienceLevels = [100];
        var packet = new RecordingPacket();

        new PetInventoryComposer(PetAppearanceSnapshots.Inventory([pet])).Compose(packet);

        Assert.Equal(expectedFigure, pet.Look);
        Assert.Equal(expectedFigure.Split(' ').Skip(3).Select(int.Parse).Cast<object>(), packet.Writes.Skip(9).SkipLast(1));
        Assert.Equal(1, packet.Writes.Last());
    }

    [Fact]
    public void PetInfoStatusTailMatchesTheClientBooleanAndIntegerOrder()
    {
        var habbo = new Habbo
        {
            Id = 42,
            Username = "LifePet",
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 12, 0, 0, 0, "", 0)
        };
        var packet = new RecordingPacket();

        new PetInformationComposer(PetInformationService.Capture(habbo, DateTimeOffset.UtcNow)).Compose(packet);

        // PetInfoMessageParser reads the status tail after rarity, saddle, and rider.
        Assert.Equal(new object[] { 0, 0, false, true, false, 0, -1, -1, -1, false }, packet.Writes.Skip(17));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void HorseAppearanceFreezesExactCustomPartFields(int saddle)
    {
        var pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        pet.VirtualId = 4;
        pet.PetId = 42;
        pet.Type = 12;
        pet.Race = "2";
        pet.Color = "ABCDEF";
        pet.PetHair = 7;
        pet.HairDye = 8;
        pet.Saddle = saddle;
        var user = (RoomUser)RuntimeHelpers.GetUninitializedObject(typeof(RoomUser));
        user.PetData = pet;
        user.RidingHorse = true;
        var composer = new PetHorseFigureInformationComposer(PetAppearanceSnapshots.Horse(user));
        var packet = new RecordingPacket();
        composer.Compose(packet);
        var expected = new List<object> { 4, 42, 12, 2, "abcdef" };
        expected.AddRange(saddle > 0 ? new object[] { 4, 3, 3, 7, 8, 2, 7, 8, 4, 9, 0 }
            : new object[] { 1, 2, 2, 7, 8, 3, 7, 8 });
        expected.AddRange(new object[] { saddle > 0, true });
        Assert.Equal(expected, packet.Writes);
        pet.Race = "invalid";
        pet.Saddle = 100;
        pet.HairDye = 0;
        user.RidingHorse = false;
        var repeated = new RecordingPacket();
        composer.Compose(repeated);
        Assert.Equal(packet.Writes, repeated.Writes);
    }

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = [];
        public int MessageId
        {
            get; set;
        }
        public ReadOnlyMemory<byte> Buffer => ReadOnlyMemory<byte>.Empty;
        public void WriteByte(byte value) => Writes.Add(value);
        public void WriteShort(short value) => Writes.Add(value);
        public void WriteInt(int value) => Writes.Add(value);
        public void WriteInteger(int value) => Writes.Add(value);
        public void WriteUInt(uint value) => Writes.Add(value);
        public void WriteUInteger(uint value) => Writes.Add(value);
        public void WriteBool(bool value) => Writes.Add(value);
        public void WriteBoolean(bool value) => Writes.Add(value);
        public void WriteString(string value) => Writes.Add(value ?? "");
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
