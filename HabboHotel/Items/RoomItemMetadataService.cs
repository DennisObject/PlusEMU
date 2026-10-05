using System.Collections.Immutable;
using Dapper;
using Plus.Core.FigureData;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Subscriptions;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items;

public sealed record MannequinNameRequest(uint ItemId, string Name);
public sealed record TonerSettingsRequest(uint ItemId, int Hue, int Saturation, int Lightness);
/// <summary>Branding save request; <c>Values</c> is the flat key,value list, or null when the frame carried only the item id.</summary>
public sealed record BrandingRequest(uint ItemId, ImmutableArray<string>? Values);

public interface IRoomItemMetadataStore
{
    void SetMannequinData(uint itemId, uint roomId, string data);
    void SetToner(uint itemId, uint roomId, int hue, int saturation, int lightness);
    void SetBrandingData(uint itemId, uint roomId, string data);
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

    // The row must exist in the room; MySQL reports zero affected rows for an identical value, so existence is checked instead of the update count.
    public void SetBrandingData(uint itemId, uint roomId, string data)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (connection.Query<int>("SELECT id FROM items WHERE id=@itemId AND room_id=@roomId FOR UPDATE", new { itemId, roomId }, transaction).Count() != 1)
            throw new InvalidOperationException("Branding item is not in the room.");
        connection.Execute("UPDATE items SET extra_data=@data WHERE id=@itemId AND room_id=@roomId LIMIT 1", new { itemId, roomId, data }, transaction);
        transaction.Commit();
    }
}

public interface IRoomItemMetadataService
{
    void SetMannequinName(GameClient session, MannequinNameRequest request);
    void SetMannequinFigure(GameClient session, uint itemId);
    void SetToner(Room room, GameClient session, TonerSettingsRequest request);
    void SetBranding(GameClient session, BrandingRequest request);
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

    public void SetBranding(GameClient session, BrandingRequest request)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;
        if (!habbo.InRoom || room == null || !room.CheckRights(session, true) || !habbo.Access.Can(PermissionKeys.RoomItemSaveBrandingItems)) return;
        var item = room.GetRoomItemHandler().GetItem(request.ItemId);
        if (item == null || item.IsTemporary) return;
        if (item.Definition.InteractionType != InteractionType.Background)
        {
            // Non-background furniture keeps the placement-only republish that an id-only frame has always caused.
            room.GetRoomItemHandler().SetFloorItem(session, item, item.GetX, item.GetY, item.Rotation, false, false, true);
            return;
        }
        if (request.Values is not { } values || FurniExtraData.RejectsClientImage(values)) return;
        var pairs = new Dictionary<string, string> { ["state"] = "0" };
        for (var index = 0; index < values.Length; index += 2) pairs[values[index]] = values[index + 1];
        var data = new MapDataFormat(pairs);
        var serialized = data.Serialize();
        room.GetRoomItemHandler().SetFloorItemData(session, item, data, () => store.SetBrandingData(item.Id, room.Id, serialized));
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
