using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Incoming.Rooms.AI.Pets;
using Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class PetInformationSnapshotTests
{
    [Fact]
    public void PetInformationPreservesFieldsAndIsStableAfterPetMutation()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(2_200_000_000);
        var pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        pet.PetId = 42;
        pet.Name = "Horse";
        pet.OwnerId = 7;
        pet.OwnerName = "Owner";
        pet.Experience = 300;
        pet.ExperienceLevels = [100, 200, 400];
        pet.Energy = 55;
        pet.Nutrition = 60;
        pet.Respect = 8;
        pet.Saddle = 9;
        pet.AnyoneCanRide = 1;
        pet.CreatedAt = now.AddDays(-2);
        var composer = new PetInformationComposer(new PetInformationService(new FixedClock(now)).Capture(pet));
        var expected = new object[] { 42, "Horse", 3, 20, 300, 400, 55, 100, 60, 150, 8, 7, 2, "Owner", 1,
            true, false, 0, 1, false, true, false, 0, -1, -1, -1, false };
        Assert.Equal(expected, Writes(composer));
        pet.Name = "Changed";
        pet.Experience = 0;
        pet.ExperienceLevels[2] = 999;
        pet.OwnerName = "changed";
        pet.Saddle = 0;
        pet.AnyoneCanRide = 0;
        Assert.Equal(expected, Writes(composer));
        Assert.Equal(expected, Writes(composer));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(2_200_086_400L, 0)]
    [InlineData(2_199_827_200L, 2)]
    public void PetAgeHandlesNullAndFutureDates(long? created, int expectedAge)
    {
        var pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        pet.ExperienceLevels = [100];
        pet.CreatedAt = created.HasValue ? DateTimeOffset.FromUnixTimeSeconds(created.Value) : null;
        var snapshot = new PetInformationService(new FixedClock(DateTimeOffset.FromUnixTimeSeconds(2_200_000_000))).Capture(pet);
        Assert.Equal(expectedAge, snapshot.AgeInDays);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(2_200_086_400L, 0)]
    [InlineData(2_199_827_200L, 2)]
    public void AccountAgeHandlesNullAndFutureDates(long? created, int expectedAge)
    {
        var habbo = new Habbo
        {
            Id = 7,
            Username = "Owner",
            AccountCreatedAt = created.HasValue ? DateTimeOffset.FromUnixTimeSeconds(created.Value) : null,
            HabboStats = new(0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0)
        };
        var snapshot = PetInformationService.Capture(habbo, DateTimeOffset.FromUnixTimeSeconds(2_200_000_000));
        Assert.Equal(expectedAge, snapshot.AgeInDays);
        Assert.Equal((7, "Owner", 8, false, 0), (snapshot.OwnerId, snapshot.OwnerName, snapshot.Respect, snapshot.HasSaddle, snapshot.AnyoneCanRide));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void AccountAgeCountsCompleteDaysAcrossOffsetsAndFractionalSeconds(int ticksFromDay, int expected)
    {
        var createdAt = new DateTimeOffset(2040, 1, 2, 0, 0, 0, TimeSpan.FromHours(9)).AddTicks(9_000_000);
        var habbo = new Habbo { AccountCreatedAt = createdAt, HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0) };
        var now = createdAt.ToOffset(TimeSpan.FromHours(-7)).AddDays(1).AddTicks(ticksFromDay);
        Assert.Equal(expected, PetInformationService.Capture(habbo, now).AgeInDays);
    }

    [Fact]
    public void LargePetAndAccountAgesTruncateTicksBeforeCountingDays()
    {
        var created = DateTimeOffset.MinValue;
        var now = created.AddDays(3_000_000).AddTicks(-1);
        var pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        pet.ExperienceLevels = [100];
        pet.CreatedAt = created;
        var habbo = new Habbo { AccountCreatedAt = created, HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0) };
        Assert.Equal(2_999_999, new PetInformationService(new FixedClock(now)).Capture(pet).AgeInDays);
        Assert.Equal(2_999_999, PetInformationService.Capture(habbo, now).AgeInDays);
    }

    [Fact]
    public async Task IncomingPetRequestOnlyDecodesAndDelegates()
    {
        var pets = new RecordingService();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        await new GetPetInformationEvent(pets).Parse(client, HabbiconTestSupport.Incoming(42));
        Assert.Equal((client, 42), pets.Request);
        Assert.Empty(sent);
    }

    [Fact]
    public void RequestOutsideRoomDoesNotPublishInformation()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        new PetInformationService(new FixedClock(DateTimeOffset.UnixEpoch)).SendInformation(client, 42);
        Assert.Empty(sent);
    }

    private static object[] Writes(PetInformationComposer composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);

        return packet.Writes.ToArray();
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class RecordingService : IPetInformationService
    {
        public (GameClient Session, int PetId)? Request { get; private set; }
        public void SendInformation(GameClient session, int petId) => Request = (session, petId);
        public void SendTrainingPanel(GameClient session, int petId) => throw new NotSupportedException();
        public PetInformationSnapshot Capture(Pet pet) => throw new NotSupportedException();
    }
}
