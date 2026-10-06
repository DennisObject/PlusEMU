using Plus.Core;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Commands.User;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class ServerUptimeTests
{
    [Fact]
    public void StartupCapturesUtcAndMonotonicTimestampOnce()
    {
        var clock = new Clock();
        var uptime = new ServerUptime(clock);
        Assert.Null(uptime.StartedAt);
        Assert.Equal(TimeSpan.Zero, uptime.Elapsed);
        Assert.Equal(0, clock.UtcReads);
        Assert.Equal(0, clock.TimestampReads);

        uptime.Start();
        uptime.Start();

        Assert.Equal(clock.Now, uptime.StartedAt);
        Assert.Equal(1, clock.UtcReads);
        Assert.Equal(1, clock.TimestampReads);
    }

    [Fact]
    public void UptimeUsesElapsedTimeAcrossUtcClockChangesAndClampsNegativeValues()
    {
        var clock = new Clock();
        var uptime = new ServerUptime(clock);
        uptime.Start();
        clock.Timestamp += 90_000;
        clock.Now = clock.Now.AddDays(-2);
        Assert.Equal(TimeSpan.FromSeconds(90), uptime.Elapsed);
        clock.Now = clock.Now.AddYears(20);
        Assert.Equal(TimeSpan.FromSeconds(90), uptime.Elapsed);
        Assert.Equal(1, clock.UtcReads);
        clock.Timestamp = 0;
        Assert.Equal(TimeSpan.Zero, uptime.Elapsed);
    }

    [Fact]
    public void InformationCommandReportsTheSharedElapsedTime()
    {
        var clock = new Clock();
        var uptime = new ServerUptime(clock);
        uptime.Start();
        clock.Timestamp += (long)TimeSpan.FromDays(2).Add(TimeSpan.FromHours(3)).Add(TimeSpan.FromMinutes(4)).TotalMilliseconds;
        var clients = CatalogSnapshotTestSupport.Proxy<IGameClientManager>((method, _) => method == "get_Count" ? 5 : null);
        var rooms = CatalogSnapshotTestSupport.Proxy<IRoomManager>((method, _) => method == "get_Count" ? 6 : null);
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        new InfoCommand(clients, rooms, uptime).Execute(client, null!, []);
        var packet = Assert.Single(sent);
        var reader = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = packet.Payload };
        reader.ReadString();
        var count = reader.ReadInt();
        var fields = new Dictionary<string, string>();

        for (var index = 0; index < count; index++)
        {
            fields.Add(reader.ReadString(), reader.ReadString());
        }

        Assert.Contains(fields.Values, text => text.Contains("Uptime: 2 day(s), 3 hours and 4 minutes.", StringComparison.Ordinal));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2040-01-01T00:00:00Z");
        public long Timestamp = 1_000;
        public int UtcReads;
        public int TimestampReads;
        public override long TimestampFrequency => 1_000;
        public override DateTimeOffset GetUtcNow()
        {
            UtcReads++;

            return Now;
        }
        public override long GetTimestamp()
        {
            TimestampReads++;

            return Timestamp;
        }
    }
}
