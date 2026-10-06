using Dapper;
using Plus.Core.FigureData;
using Plus.HabboHotel.Subscriptions;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items;

public sealed record MannequinNameRequest(uint ItemId, string Name);
public sealed record TonerSettingsRequest(uint ItemId, int Hue, int Saturation, int Lightness);

public interface IRoomItemMetadataStore
{
    void SetMannequinData(uint itemId, uint roomId, string data);
    void SetToner(uint itemId, uint roomId, int hue, int saturation, int lightness);
}

public sealed class RoomItemMetadataStore(IDatabase database) : IRoomItemMetadataStore
{
    public void SetMannequinData(uint itemId, uint roomId, string data)
    {
        using var connection = database.Connection();
        if (connection.Execute("UPDATE items SET extra_data=@data WHERE id=@itemId AND room_id=@roomId LIMIT 1", new { itemId, roomId, data }) != 1)
            throw new InvalidOperationException("Mannequin data was not persisted.");
    }

    public void SetToner(uint itemId, uint roomId, int hue, int saturation, int lightness)
    {
        using var connection = database.Connection();
        if (connection.Execute("UPDATE room_items_toner toner JOIN items item ON item.id=toner.id SET toner.enabled=TRUE,toner.data1=@hue,toner.data2=@saturation,toner.data3=@lightness WHERE toner.id=@itemId AND item.room_id=@roomId",
                new { itemId, roomId, hue, saturation, lightness }) != 1)
            throw new InvalidOperationException("Toner data was not persisted.");
    }
}

public interface IRoomItemMetadataService
{
    void SetMannequinName(GameClient session, MannequinNameRequest request);
    void SetMannequinFigure(GameClient session, uint itemId);
    void SetToner(Room room, GameClient session, TonerSettingsRequest request);
}

public sealed class RoomItemMetadataService(IRoomItemMetadataStore store, IFigureDataManager figures) : IRoomItemMetadataService
{
    public void SetMannequinFigure(GameClient session, uint itemId)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;
        if (room == null || !room.CheckRights(session, true))
            return;
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary || item.Definition?.InteractionType != InteractionType.Mannequin)
            return;
        var fields = item.LegacyDataString.Split((char)5);
        if (fields.Length == 2)
            return;
        var name = fields.Length >= 3 ? fields[2] : "Default";
        var figure = string.Join('.', figures.ProcessFigure(habbo.Look, habbo.Gender,
                habbo.Clothing.GetClothingParts, ClubAccess.LevelFor(habbo.Access))
            .Split('.').Where(part => !part.Contains("hr") && !part.Contains("hd") && !part.Contains("he")
                && !part.Contains("ea") && !part.Contains("ha"))).TrimEnd('.');
        var data = $"{habbo.Gender.ToLowerInvariant()}{(char)5}{figure}{(char)5}{name}";
        store.SetMannequinData(item.Id, room.Id, data);
        item.LegacyDataString = data;
        item.UpdateState(true, true);
    }

    public void SetMannequinName(GameClient session, MannequinNameRequest request)
    {
        var room = session.GetHabbo().CurrentRoom;
        if (room == null || !room.CheckRights(session, true)) return;
        var item = room.GetRoomItemHandler().GetItem(request.ItemId);
        if (item == null || item.IsTemporary || item.Definition?.InteractionType != InteractionType.Mannequin) return;

        var fields = item.LegacyDataString.Split((char)5);
        var data = fields.Length >= 2
            ? $"{fields[0]}{(char)5}{fields[1]}{(char)5}{request.Name}"
            : $"m{(char)5}.ch-210-1321.lg-285-92{(char)5}{request.Name}";
        store.SetMannequinData(item.Id, room.RoomId, data);
        item.LegacyDataString = data;
        item.UpdateState(true, true);
    }

    public void SetToner(Room room, GameClient session, TonerSettingsRequest request)
    {
        if (!room.CheckRights(session, true) || room.TonerData == null || room.TonerData.ItemId != request.ItemId ||
            request.Hue is < 0 or > 255 || request.Saturation is < 0 or > 255 || request.Lightness is < 0 or > 255) return;
        var item = room.GetRoomItemHandler().GetItem(request.ItemId);
        if (item == null || item.IsTemporary || item.Definition?.InteractionType != InteractionType.Toner) return;

        store.SetToner(item.Id, room.Id, request.Hue, request.Saturation, request.Lightness);
        room.TonerData.Hue = request.Hue;
        room.TonerData.Saturation = request.Saturation;
        room.TonerData.Lightness = request.Lightness;
        room.TonerData.Enabled = 1;
        room.SendPacket(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)));
        item.UpdateState();
    }
}
