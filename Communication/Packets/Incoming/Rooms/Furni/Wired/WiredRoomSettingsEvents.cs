using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

public sealed class WiredRoomSettingsRequestEvent(IWiredRoomSettingsService settings) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!packet.HasDataRemaining()) {
            settings.Reload(room, session);
        }

        return Task.CompletedTask;
    }
}

public sealed class WiredRoomSettingsSaveEvent(IWiredRoomSettingsService settings) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        int inspect, modify;

        try {
            inspect = packet.ReadInt();
            modify = packet.ReadInt();
        }
        catch (ArgumentException) {
            return Task.CompletedTask;
        }

        if (packet.HasDataRemaining()) {
            return Task.CompletedTask;
        }

        settings.Save(room, session, inspect, modify, null);

        return Task.CompletedTask;
    }
}

public sealed class WiredMenuPermissionsSaveEvent(IWiredRoomSettingsService settings) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        int modify, inspect;
        string timezone;

        try {
            modify = packet.ReadInt();
            inspect = packet.ReadInt();
            timezone = packet.ReadString();
        }
        catch (ArgumentException) {
            return Task.CompletedTask;
        }

        if (packet.HasDataRemaining()) {
            return Task.CompletedTask;
        }

        settings.Save(room, session, inspect, modify, timezone);

        return Task.CompletedTask;
    }
}
