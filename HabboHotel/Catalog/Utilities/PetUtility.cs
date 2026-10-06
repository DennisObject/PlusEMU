using Dapper;
using System.Data;
using Plus.HabboHotel.Rooms.AI;
using Plus.Utilities;

namespace Plus.HabboHotel.Catalog.Utilities;

public static class PetUtility
{
    public static bool CheckPetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.Length < 1 || name.Length > 16)
        {
            return false;
        }

        if (!StringCharFilter.IsValidAlphaNumeric(name))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// A pet purchase's extra data is <c>name\npaletteId\ncolor</c>. The palette id is the breed
    /// the client picked; it is not limited to the two-digit races of the old catalog.
    /// </summary>
    public static bool TryReadPurchase(string extraData, out string name, out string race, out string color)
    {
        name = "";
        race = "";
        color = "";

        if (string.IsNullOrEmpty(extraData))
        {
            return false;
        }

        var bits = extraData.Split('\n');

        if (bits.Length < 3 || !CheckPetName(bits[0]))
        {
            return false;
        }

        if (bits[1].Length is < 1 or > 4 || !bits[1].All(char.IsAsciiDigit))
        {
            return false;
        }

        if (bits[2].Length != 6 || !bits[2].All(Uri.IsHexDigit))
        {
            return false;
        }

        name = bits[0];
        race = bits[1];
        color = bits[2];

        return true;
    }

    /// <summary>
    /// Inserts the bot and its pet row together. <c>bots.name</c>, <c>motto</c> and <c>look</c> are required;
    /// a failed insert returns no pet instead of id 0.
    /// </summary>
    internal static Pet? CreatePet(IDbConnection connection, IDbTransaction transaction, DateTimeOffset createdAt,
        string ownerName, int userId, string name,
        int type, string race, string colour, PetBoxLocation? gnomeBox = null, string gnomeClothing = "-1")
    {
        createdAt = createdAt.ToUniversalTime();
        var roomId = gnomeBox?.RoomId ?? 0;
        var x = gnomeBox?.X ?? 0;
        var y = gnomeBox?.Y ?? 0;
        var z = gnomeBox?.Z ?? 0;
        var parts = gnomeBox == null ? "2 2 -1 0 3 -1 0" : gnomeClothing;
        var look = $"{type} {race} {colour} {parts}";
        var inserted = connection.Execute(
            "INSERT INTO `bots` (`user_id`,`room_id`,`name`,`motto`,`look`,`x`,`y`,`z`,`ai_type`) VALUES (@UserId,@RoomId,@Name,'',@Look,@X,@Y,@Z,'pet')",
            new
            {
                UserId = userId,
                RoomId = roomId,
                Name = name,
                Look = look,
                X = x,
                Y = y,
                Z = z
            },
            transaction);
        var petId = connection.ExecuteScalar<long>("SELECT LAST_INSERT_ID()", transaction: transaction);

        if (inserted != 1 || petId <= 0 || petId > int.MaxValue)
        {
            return null;
        }

        var id = (int)petId;
        connection.Execute(
            "INSERT INTO `bots_petdata` (`id`,`type`,`race`,`color`,`experience`,`energy`,`nutrition`,`respect`,`createstamp`,`have_saddle`,`anyone_ride`,`hairdye`,`pethair`,`gnome_clothing`) VALUES (@Id,@Type,@Race,@Color,0,100,100,0,@Created,0,0,0,-1,@GnomeClothing)",
            new
            {
                Id = id,
                Type = type,
                Race = race,
                Color = colour,
                Created = createdAt.UtcDateTime,
                GnomeClothing = gnomeClothing
            },
            transaction);

        if (gnomeBox != null && connection.Execute(
            "DELETE FROM `items` WHERE `id`=@Id AND `user_id`=@OwnerId AND `room_id`=@RoomId AND `base_item`=@BaseItem LIMIT 1",
            new
            {
                Id = gnomeBox.Value.ItemId,
                OwnerId = userId,
                RoomId = roomId,
                BaseItem = gnomeBox.Value.BaseItem
            }, transaction) != 1)
        {
            return null;
        }

        return new Pet(id, userId, roomId, name, type, race, colour, 0, 100, 100, 0, createdAt, x, y, z, 0, 0, 0, -1, gnomeClothing, ownerName);
    }

}

internal readonly record struct PetBoxLocation(uint ItemId, uint BaseItem, uint RoomId, int X, int Y, double Z);
