using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Outgoing.Inventory.Pets;
using Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;
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

        new PetInventoryComposer([pet]).Compose(packet);

        Assert.Equal(new object[] { 1, 0, 1, 42, "LifePet", 12, 2, "FFFFFF", 0, 2, 2, -1, 0, 3, -1, 0, 3 }, packet.Writes);
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

        new PetInventoryComposer([pet]).Compose(packet);

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

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = [];
        public int MessageId { get; set; }
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
