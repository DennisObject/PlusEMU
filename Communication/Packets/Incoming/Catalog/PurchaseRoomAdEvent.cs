using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users.Messenger;
using Dapper;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Friends;
using Plus.Core.Settings;

namespace Plus.Communication.Packets.Incoming.Catalog;

public class PurchaseRoomAdEvent : IPacketEvent
{
    private readonly IWordFilterManager _wordFilterManager;
    private readonly IDatabase _database;
    private readonly IBadgeManager _badgeManager;
    private readonly IMessengerDataLoader _messengerDataLoader;
    private readonly ISettingsManager _settings;
    private readonly TimeProvider _clock;

    public PurchaseRoomAdEvent(IWordFilterManager wordFilterManager, IDatabase database, IBadgeManager badgeManager,
        IMessengerDataLoader messengerDataLoader, ISettingsManager settings, TimeProvider clock)
    {
        _wordFilterManager = wordFilterManager;
        _database = database;
        _badgeManager = badgeManager;
        _messengerDataLoader = messengerDataLoader;
        _settings = settings;
        _clock = clock;
    }

    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        packet.ReadInt(); //pageId
        packet.ReadInt(); //itemId
        var roomId = packet.ReadUInt();
        var name = _wordFilterManager.CheckMessage(packet.ReadString());
        packet.ReadBool(); //junk
        var desc = _wordFilterManager.CheckMessage(packet.ReadString());
        var categoryId = packet.ReadInt();
        if (!RoomFactory.TryGetData(roomId, out var data))
            return;
        if (data.OwnerId != session.GetHabbo().Id)
            return;
        var now = _clock.GetUtcNow();
        if (data.Promotion == null)
        {
            var lifespan = TimeSpan.FromMinutes(Convert.ToInt32(_settings.TryGetValue("room.promotion.lifespan")));
            data.Promotion = new(name, desc, categoryId, now, now + lifespan, _clock);
        }
        else
        {
            data.Promotion.Name = name;
            data.Promotion.Description = desc;
            data.Promotion.Extend(TimeSpan.FromHours(2));
        }
        using (var connection = _database.Connection())
        {
            connection.Execute(
                "REPLACE INTO `room_promotions` (`room_id`,`title`,`description`,`timestamp_start`,`timestamp_expire`,`category_id`) VALUES (@roomId, @title, @description, @start, @expires, @categoryId)",
                new
                {
                    roomId,
                    title = name,
                    description = desc,
                    start = data.Promotion.StartedAt?.UtcDateTime,
                    expires = data.Promotion.ExpiresAt?.UtcDateTime,
                    categoryId
                });
        }
        if (!session.GetHabbo().Inventory.Badges.HasBadge("RADZZ"))
            await _badgeManager.GiveBadge(session.GetHabbo(), "RADZZ");
        session.Send(new PurchaseOKComposer());
        if (session.GetHabbo().InRoom && session.GetHabbo().CurrentRoom.Id == roomId)
            session.GetHabbo().CurrentRoom?.SendPacket(new RoomEventComposer(RoomEventSnapshot.Capture(data, data.Promotion)));
        _messengerDataLoader.BroadcastStatusUpdate(session.GetHabbo(), MessengerEventTypes.EventStarted, name);
    }
}
