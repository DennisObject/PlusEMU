using System.Collections.Immutable;
using Dapper;
using Plus.Core.FigureData;
using Plus.HabboHotel.Items.Data.Moodlight;
using Plus.HabboHotel.Items.Data.Toner;
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
    MoodlightRecord? LoadMoodlight(uint itemId);
    void SetMoodlightEnabled(uint itemId, uint roomId, bool enabled);
    void UpdateMoodlightPreset(uint itemId, uint roomId, int preset, string value);
    TonerRecord? LoadToner(uint itemId);
}

public sealed class RoomItemMetadataStore(IDatabase database) : IRoomItemMetadataStore
{
    public void SetMannequinData(uint itemId, uint roomId, string data)
    {
        using var connection = database.Connection();

        if (connection.Execute("UPDATE items SET extra_data=@data WHERE id=@itemId AND room_id=@roomId LIMIT 1", new
        {
            itemId,
            roomId,
            data
        }) != 1)
        {
            throw new InvalidOperationException("Mannequin data was not persisted.");
        }
    }

    public void SetToner(uint itemId, uint roomId, int hue, int saturation, int lightness)
    {
        using var connection = database.Connection();

        if (connection.Execute("UPDATE room_items_toner toner JOIN items item ON item.id=toner.id SET toner.enabled=TRUE,toner.data1=@hue,toner.data2=@saturation,toner.data3=@lightness WHERE toner.id=@itemId AND item.room_id=@roomId",
                new
                {
                    itemId,
                    roomId,
                    hue,
                    saturation,
                    lightness
                }) != 1)
        {
            throw new InvalidOperationException("Toner data was not persisted.");
        }
    }

    // The row must exist in the room; MySQL reports zero affected rows for an identical value, so existence is checked instead of the update count.
    public void SetBrandingData(uint itemId, uint roomId, string data)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (connection.Query<uint>("SELECT id FROM items WHERE id=@itemId AND room_id=@roomId FOR UPDATE", new
        {
            itemId,
            roomId
        }, transaction).Count() != 1)
        {
            throw new InvalidOperationException("Branding item is not in the room.");
        }

        connection.Execute("UPDATE items SET extra_data=@data WHERE id=@itemId AND room_id=@roomId LIMIT 1", new
        {
            itemId,
            roomId,
            data
        }, transaction);
        transaction.Commit();
    }

    // Loads the lowest sidecar row for the item, creating the default row when none exists. A missing item is refused, not created.
    public MoodlightRecord? LoadMoodlight(uint itemId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (!ItemExists(connection, transaction, itemId))
        {
            return null;
        }

        var row = FirstSidecar(connection, transaction, itemId);

        if (row == null)
        {
            connection.Execute("INSERT INTO room_items_moodlight (item_id,enabled,current_preset,preset_one,preset_two,preset_three) VALUES (@itemId,FALSE,1,'#000000,255,0','#000000,255,0','#000000,255,0')",
                new
                {
                    itemId
                }, transaction);
            row = FirstSidecar(connection, transaction, itemId) ?? throw new InvalidOperationException("Moodlight defaults were not persisted.");
        }

        transaction.Commit();

        return new(row.SidecarId, row.Enabled, row.CurrentPreset, row.PresetOne, row.PresetTwo, row.PresetThree);
    }

    // Exact item and room, then the lowest sidecar: duplicates are kept, only the chosen row is written.
    public void SetMoodlightEnabled(uint itemId, uint roomId, bool enabled)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        RequireItemInRoom(connection, transaction, itemId, roomId);
        var row = FirstSidecar(connection, transaction, itemId) ?? throw new InvalidOperationException("Moodlight sidecar was not found.");
        connection.Execute("UPDATE room_items_moodlight SET enabled=@enabled WHERE id=@sidecarId LIMIT 1", new
        {
            enabled,
            sidecarId = row.SidecarId
        }, transaction);
        transaction.Commit();
    }

    // Writes enabled, current preset and only the selected preset column; untouched presets keep their stored text.
    public void UpdateMoodlightPreset(uint itemId, uint roomId, int preset, string value)
    {
        var column = preset switch
        {
            1 => "preset_one",
            2 => "preset_two",
            3 => "preset_three",
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        RequireItemInRoom(connection, transaction, itemId, roomId);
        var row = FirstSidecar(connection, transaction, itemId) ?? throw new InvalidOperationException("Moodlight sidecar was not found.");
        // An identical value changes no rows; the locked, existing sidecar is the contract.
        connection.Execute($"UPDATE room_items_moodlight SET enabled=TRUE, current_preset=@preset, {column}=@value WHERE id=@sidecarId LIMIT 1",
            new
            {
                preset,
                value,
                sidecarId = row.SidecarId
            }, transaction);
        transaction.Commit();
    }

    public TonerRecord? LoadToner(uint itemId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (!ItemExists(connection, transaction, itemId))
        {
            return null;
        }

        var row = FirstToner(connection, transaction, itemId);

        if (row == null)
        {
            connection.Execute("INSERT INTO room_items_toner (id,enabled,data1,data2,data3) VALUES (@itemId,FALSE,0,0,0)", new
            {
                itemId
            }, transaction);
            row = FirstToner(connection, transaction, itemId) ?? throw new InvalidOperationException("Toner defaults were not persisted.");
        }

        transaction.Commit();

        return new()
        {
            Enabled = row.Enabled,
            Hue = row.Hue,
            Saturation = row.Saturation,
            Lightness = row.Lightness
        };
    }

    private static bool ItemExists(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, uint itemId) =>
        connection.Query<uint>("SELECT id FROM items WHERE id = @itemId FOR UPDATE", new
        {
            itemId
        }, transaction).Count() == 1;

    private static void RequireItemInRoom(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, uint itemId, uint roomId)
    {
        if (connection.Query<uint>("SELECT id FROM items WHERE id = @itemId AND room_id = @roomId FOR UPDATE", new
        {
            itemId,
            roomId
        }, transaction).Count() != 1)
        {
            throw new InvalidOperationException("Item is not in the room.");
        }
    }

    private static SidecarRow? FirstSidecar(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, uint itemId) =>
        connection.Query<SidecarRow>("SELECT id AS SidecarId,enabled AS Enabled,current_preset AS CurrentPreset,preset_one AS PresetOne,preset_two AS PresetTwo,preset_three AS PresetThree FROM room_items_moodlight WHERE item_id = @itemId ORDER BY id LIMIT 1 FOR UPDATE",
            new
            {
                itemId
            }, transaction).FirstOrDefault();

    private static TonerRow? FirstToner(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, uint itemId) =>
        connection.Query<TonerRow>("SELECT enabled AS Enabled,data1 AS Hue,data2 AS Saturation,data3 AS Lightness FROM room_items_toner WHERE id = @itemId FOR UPDATE",
            new
            {
                itemId
            }, transaction).FirstOrDefault();

    private sealed record SidecarRow(int SidecarId, bool Enabled, int CurrentPreset, string PresetOne, string PresetTwo, string PresetThree);
    private sealed class TonerRow
    {
        public bool Enabled
        {
            get; init;
        }
        public int Hue
        {
            get; init;
        }
        public int Saturation
        {
            get; init;
        }
        public int Lightness
        {
            get; init;
        }
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
        {
            return;
        }

        var item = room.GetRoomItemHandler().GetItem(itemId);

        if (item == null || item.IsTemporary || item.Definition?.InteractionType != InteractionType.Mannequin)
        {
            return;
        }

        var fields = item.LegacyDataString.Split((char)5);

        if (fields.Length == 2)
        {
            return;
        }

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

        if (room == null || !room.CheckRights(session, true))
        {
            return;
        }

        var item = room.GetRoomItemHandler().GetItem(request.ItemId);

        if (item == null || item.IsTemporary || item.Definition?.InteractionType != InteractionType.Mannequin)
        {
            return;
        }

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

        if (!habbo.InRoom || room == null || !room.CheckRights(session, true) || !habbo.Access.Can(PermissionKeys.RoomItemSaveBrandingItems))
        {
            return;
        }

        var item = room.GetRoomItemHandler().GetItem(request.ItemId);

        if (item == null || item.IsTemporary)
        {
            return;
        }

        if (item.Definition.InteractionType != InteractionType.Background)
        {
            // Non-background furniture keeps the placement-only republish that an id-only frame has always caused.
            room.GetRoomItemHandler().SetFloorItem(session, item, item.GetX, item.GetY, item.Rotation, false, false, true);

            return;
        }

        if (request.Values is not { } values || FurniExtraData.RejectsClientImage(values))
        {
            return;
        }

        var pairs = new Dictionary<string, string> { ["state"] = "0" };

        for (var index = 0; index < values.Length; index += 2)
        {
            pairs[values[index]] = values[index + 1];
        }

        var data = new MapDataFormat(pairs);
        var serialized = data.Serialize();
        room.GetRoomItemHandler().SetFloorItemData(session, item, data, () => store.SetBrandingData(item.Id, room.Id, serialized));
    }

    public void SetToner(Room room, GameClient session, TonerSettingsRequest request)
    {
        if (!room.CheckRights(session, true) || room.TonerData == null || room.TonerData.ItemId != request.ItemId ||
            request.Hue is < 0 or > 255 || request.Saturation is < 0 or > 255 || request.Lightness is < 0 or > 255)
        {
            return;
        }

        var item = room.GetRoomItemHandler().GetItem(request.ItemId);

        if (item == null || item.IsTemporary || item.Definition?.InteractionType != InteractionType.Toner)
        {
            return;
        }

        store.SetToner(item.Id, room.Id, request.Hue, request.Saturation, request.Lightness);
        room.TonerData.Hue = request.Hue;
        room.TonerData.Saturation = request.Saturation;
        room.TonerData.Lightness = request.Lightness;
        room.TonerData.Enabled = 1;
        room.SendPacket(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)));
        item.UpdateState();
    }
}
