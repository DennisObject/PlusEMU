using System.Data.Common;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

public sealed class WiredRoomSettingsRequestEvent(IDatabase database, ILoggerFactory loggerFactory) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!packet.HasDataRemaining()) WiredRoomSettingsPackets.Reload(room, session, WiredRoomSettings.For(room, database), loggerFactory.CreateLogger(nameof(WiredRoomSettingsPackets)));
        return Task.CompletedTask;
    }
}

public sealed class WiredRoomSettingsSaveEvent(IDatabase database, ILoggerFactory loggerFactory) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        int inspect, modify;
        try { inspect = packet.ReadInt(); modify = packet.ReadInt(); }
        catch (ArgumentException) { return Task.CompletedTask; }
        if (packet.HasDataRemaining()) return Task.CompletedTask;
        var settings = WiredRoomSettings.For(room, database);
        WiredRoomSettingsPackets.Save(room, session, settings, inspect, modify, null, loggerFactory.CreateLogger(nameof(WiredRoomSettingsPackets)));
        return Task.CompletedTask;
    }
}

public sealed class WiredMenuPermissionsSaveEvent(IDatabase database, ILoggerFactory loggerFactory) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        int modify, inspect; string timezone;
        try { modify = packet.ReadInt(); inspect = packet.ReadInt(); timezone = packet.ReadString(); }
        catch (ArgumentException) { return Task.CompletedTask; }
        if (packet.HasDataRemaining()) return Task.CompletedTask;
        WiredRoomSettingsPackets.Save(room, session, WiredRoomSettings.For(room, database), inspect, modify, timezone, loggerFactory.CreateLogger(nameof(WiredRoomSettingsPackets)));
        return Task.CompletedTask;
    }
}

internal static class WiredRoomSettingsPackets
{
    public static void Reply(Room room, GameClient session, WiredRoomSettings settings) =>
        session.Send(new WiredRoomSettingsDataComposer(room.Id, settings, session));

    public static void Reload(Room room, GameClient session, WiredRoomSettings settings, ILogger logger)
    {
        try
        {
            settings.Reload();
            Reply(room, session, settings);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or InvalidDataException)
        {
            logger.LogError(exception, "Unable to reload Wired settings for room {RoomId}", room.Id);
            session.Send(new WiredValidationErrorComposer("Unable to load Wired room settings."));
        }
    }

    public static void Save(Room room, GameClient session, WiredRoomSettings settings, int inspect, int modify, string? timezone, ILogger logger)
    {
        try
        {
            if (!settings.TrySave(session, inspect, modify, timezone ?? settings.Snapshot.TimeZoneId, out var error))
            {
                session.Send(new WiredValidationErrorComposer(error));
                Reply(room, session, settings);
                return;
            }
            Reply(room, session, settings);
            // Publish permissions only after the transaction and room snapshot have succeeded.
            foreach (var user in room.GetRoomUserManager().GetUserList())
                if (!user.IsBot && user.GetClient() is { } client && !ReferenceEquals(client, session))
                    Reply(room, client, settings);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or InvalidDataException)
        {
            logger.LogError(exception, "Unable to save Wired settings for room {RoomId}", room.Id);
            session.Send(new WiredValidationErrorComposer("Unable to save Wired room settings."));
            // Restore the active client's optimistic settings without broadcasting rejected changes.
            try { Reply(room, session, settings); }
            catch (Exception readError) when (readError is DbException or InvalidOperationException or InvalidDataException)
            { logger.LogError(readError, "Unable to read Wired settings for room {RoomId}", room.Id); }
        }
    }
}
