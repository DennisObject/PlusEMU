using System.Data.Common;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Settings;

public interface IWiredRoomSettingsService
{
    void Reload(Room room, GameClient session);
    void Save(Room room, GameClient session, int inspect, int modify, string? timezone);
}

public sealed class WiredRoomSettingsService(ILogger<WiredRoomSettingsService> logger) : IWiredRoomSettingsService
{
    public void Reload(Room room, GameClient session)
    {
        var settings = room.GetWired().Settings;
        try
        {
            settings.Reload();
            Reply(session, settings);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or InvalidDataException)
        {
            logger.LogError(exception, "Unable to reload Wired settings for room {RoomId}", room.Id);
            session.Send(new WiredValidationErrorComposer("Unable to load Wired room settings."));
        }
    }

    public void Save(Room room, GameClient session, int inspect, int modify, string? timezone)
    {
        var settings = room.GetWired().Settings;
        try
        {
            if (!settings.TrySave(session, inspect, modify, timezone ?? settings.Snapshot.TimeZoneId, out var error))
            {
                session.Send(new WiredValidationErrorComposer(error));
                Reply(session, settings);
                return;
            }
            Reply(session, settings);
            // Publish permissions only after the transaction, room snapshot and actor reply have succeeded.
            foreach (var user in room.GetRoomUserManager().GetUserList())
                if (!user.IsBot && user.GetClient() is { } client && !ReferenceEquals(client, session))
                    Reply(client, settings);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or InvalidDataException)
        {
            logger.LogError(exception, "Unable to save Wired settings for room {RoomId}", room.Id);
            session.Send(new WiredValidationErrorComposer("Unable to save Wired room settings."));
            // Restore the active client's optimistic settings without broadcasting rejected changes.
            try { Reply(session, settings); }
            catch (Exception readError) when (readError is DbException or InvalidOperationException or InvalidDataException)
            { logger.LogError(readError, "Unable to read Wired settings for room {RoomId}", room.Id); }
        }
    }

    private static void Reply(GameClient session, WiredRoomSettings settings) =>
        session.Send(new WiredRoomSettingsDataComposer(settings.View(session)));
}
