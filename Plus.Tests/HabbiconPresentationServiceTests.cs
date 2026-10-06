using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Habbicons;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class HabbiconPresentationServiceTests
{
    [RoomComponentDatabaseFact]
    public void DatabaseFailureLogsAndOnlyPurchaseRequestsReceiveDeliveryFailure()
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"))
        {
            Database = "information_schema",
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        var schema = "task_habbicon_presentation_" + Guid.NewGuid().ToString("N")[..12];
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        admin.Execute($"CREATE DATABASE `{schema}`");

        try
        {
            options.Database = schema;
            // The real store's missing tables cause a MySqlException before any claim or charge can commit.
            var domain = new HabbiconService(new HabbiconDatabaseTests.TestDatabase(options.ConnectionString),
                new FixedTimeProvider(FixedTimeProvider.Epoch));
            var logger = new RecordingLogger();
            var service = new HabbiconPresentationService(domain, logger);
            var habbo = new Habbo { Id = 7, Credits = 100, Access = UserAccess.Empty };
            var (client, sent) = HabbiconTestSupport.Client(habbo);
            service.ShowShop(client);
            service.ShowInfo(client, 61);
            Assert.Empty(sent);
            service.Change(client, HabbiconAction.Buy, 61);
            var response = Assert.Single(sent);
            Assert.Equal(ServerPacketHeader.PurchaseErrorComposer, response.Header);
            var body = new FlashIncomingPacket { Buffer = response.Payload };
            Assert.Equal((int)PurchaseError.DeliveryFailed, body.ReadInt());
            Assert.Equal(100, habbo.Credits);
            Assert.Equal(3, logger.Errors);
        }
        finally
        {
            admin.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class RecordingLogger : ILogger<HabbiconPresentationService>
    {
        public int Errors;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Assert.Equal(LogLevel.Error, level);
            Assert.IsType<MySqlException>(exception);
            Errors++;
        }
    }
}
