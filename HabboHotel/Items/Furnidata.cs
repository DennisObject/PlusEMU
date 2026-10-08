using System.Data;
using System.Text.Json.Nodes;
using Dapper;

namespace Plus.HabboHotel.Items;

// A furniture row's FurnitureData.json entry (migration 60). Id is the row's sprite id and Classname its item name.
// Nullable strings are written as null; the other nullable fields are left out of the entry when null.
public sealed class FurnidataEntry
{
    public const string FloorSection = "roomitemtypes";
    public const string WallSection = "wallitemtypes";

    // Fields the catalog fills in when furnidata is generated; definitions do not store them.
    public static readonly string[] CatalogFields = ["offerid", "buyout", "rentofferid", "rentbuyout", "bc"];

    public uint FurnitureId { get; set; }
    public int Id { get; set; }
    public string Classname { get; set; } = string.Empty;
    public bool IsWall { get; set; }
    public int Revision { get; set; }
    public string? Category { get; set; }
    public int DefaultDir { get; set; }
    public int XDim { get; set; } = 1;
    public int YDim { get; set; } = 1;
    public string? PartColors { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? AdUrl { get; set; }
    public bool ExcludedDynamic { get; set; }
    public string? CustomParams { get; set; }
    public int SpecialType { get; set; } = 1;
    public bool CanStandOn { get; set; }
    public bool CanSitOn { get; set; }
    public bool CanLayOn { get; set; }
    public bool? CanPutStuffOn { get; set; }
    public double? Height { get; set; }
    public string? FurniLine { get; set; }
    public string? Environment { get; set; }
    public bool Rare { get; set; }
    public bool? Tradeable { get; set; }
    public bool? Recyclable { get; set; }

    public string Section => IsWall ? WallSection : FloorSection;

    // The entry in Habbo's field order. With catalog, the purchase fields are there too (no offer yet), so the
    // catalog only has to set their values.
    public JsonObject ToJson(bool catalog)
    {
        var entry = new JsonObject { ["id"] = Id, ["classname"] = Classname, ["revision"] = Revision, ["category"] = Category };

        if (!IsWall) {
            entry["defaultdir"] = DefaultDir;
            entry["xdim"] = XDim;
            entry["ydim"] = YDim;

            if (PartColors != null) {
                entry["partcolors"] = new JsonObject
                {
                    ["color"] = new JsonArray(PartColors.Length == 0 ? [] : PartColors.Split(',').Select(color => (JsonNode)color).ToArray())
                };
            }
        }

        entry["name"] = Name;
        entry["description"] = Description;
        entry["adurl"] = AdUrl;

        if (catalog) {
            entry["offerid"] = -1;
            entry["buyout"] = false;
            entry["rentofferid"] = -1;
            entry["rentbuyout"] = false;
            entry["bc"] = false;
        }

        entry["excludeddynamic"] = ExcludedDynamic;
        entry["customparams"] = CustomParams;
        entry["specialtype"] = SpecialType;

        if (!IsWall) {
            entry["canstandon"] = CanStandOn;
            entry["cansiton"] = CanSitOn;
            entry["canlayon"] = CanLayOn;
            Optional(entry, "canputstuffon", CanPutStuffOn);
            Optional(entry, "height", Height);
        }

        entry["furniline"] = FurniLine;
        entry["environment"] = Environment;
        entry["rare"] = Rare;
        Optional(entry, "tradeable", Tradeable);
        Optional(entry, "recyclable", Recyclable);

        return entry;
    }

    // The definition fields of an entry, for the same row (furniture id and kind stay). Missing fields get the
    // column defaults; catalog fields and unknown fields are ignored.
    public FurnidataEntry WithJson(JsonObject entry) => new()
    {
        FurnitureId = FurnitureId,
        IsWall = IsWall,
        Id = Integer(entry["id"]) ?? 0,
        Classname = Text(entry["classname"]) ?? string.Empty,
        Revision = Integer(entry["revision"]) ?? 0,
        Category = Text(entry["category"]),
        DefaultDir = Integer(entry["defaultdir"]) ?? 0,
        XDim = Integer(entry["xdim"]) ?? 1,
        YDim = Integer(entry["ydim"]) ?? 1,
        PartColors = entry["partcolors"]?["color"] is JsonArray colors ? string.Join(',', colors.Select(color => Text(color) ?? string.Empty)) : null,
        Name = Text(entry["name"]),
        Description = Text(entry["description"]),
        AdUrl = Text(entry["adurl"]),
        ExcludedDynamic = Flag(entry["excludeddynamic"]) ?? false,
        CustomParams = Text(entry["customparams"]),
        SpecialType = Integer(entry["specialtype"]) ?? 1,
        CanStandOn = Flag(entry["canstandon"]) ?? false,
        CanSitOn = Flag(entry["cansiton"]) ?? false,
        CanLayOn = Flag(entry["canlayon"]) ?? false,
        CanPutStuffOn = Flag(entry["canputstuffon"]),
        Height = entry["height"] is JsonValue height && height.TryGetValue<double>(out var number) ? number : null,
        FurniLine = Text(entry["furniline"]),
        Environment = Text(entry["environment"]),
        Rare = Flag(entry["rare"]) ?? false,
        Tradeable = Flag(entry["tradeable"]),
        Recyclable = Flag(entry["recyclable"])
    };

    // Whether two entries hold the same definition, ignoring catalog fields and field order.
    public static bool SameDefinition(JsonObject first, JsonObject second) => JsonNode.DeepEquals(Definition(first), Definition(second));

    private static JsonObject Definition(JsonObject entry) =>
        new(entry.Where(field => !CatalogFields.Contains(field.Key)).Select(field => KeyValuePair.Create(field.Key, field.Value?.DeepClone())));

    private static void Optional<T>(JsonObject entry, string key, T? value) where T : struct
    {
        if (value is { } present) {
            entry[key] = JsonValue.Create(present);
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Integer(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static bool? Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;
}

// SQL for furniture rows' furnidata. A furnidata row (has_furnidata) is the entry of its kind; rows sharing its
// classname use that entry.
public sealed class FurnidataRepository(IDbConnection connection, IDbTransaction? transaction = null)
{
    private const string Columns = "id AS FurnitureId, sprite_id AS Id, item_name AS Classname, type = 'i' AS IsWall, revision AS Revision, " +
        "category AS Category, default_dir AS DefaultDir, xdim AS XDim, ydim AS YDim, part_colors AS PartColors, name AS Name, " +
        "description AS Description, ad_url AS AdUrl, excluded_dynamic AS ExcludedDynamic, custom_params AS CustomParams, " +
        "special_type AS SpecialType, can_stand_on AS CanStandOn, can_sit_on AS CanSitOn, can_lay_on AS CanLayOn, " +
        "can_put_stuff_on AS CanPutStuffOn, height AS Height, furni_line AS FurniLine, environment AS Environment, rare AS Rare, " +
        "tradeable AS Tradeable, recyclable AS Recyclable";

    // Floor entries, then wall entries, each by id.
    public List<FurnidataEntry> All() =>
        connection.Query<FurnidataEntry>($"SELECT {Columns} FROM furniture WHERE has_furnidata ORDER BY type, sprite_id", transaction: transaction).ToList();

    // The entry of a kind with this classname (compared without case, like the client).
    public FurnidataEntry? Entry(string classname, bool isWall, bool forUpdate = false) => connection.QuerySingleOrDefault<FurnidataEntry>(
        $"SELECT {Columns} FROM furniture WHERE furnidata_classname = @classname AND type = @type{(forUpdate ? " FOR UPDATE" : "")}",
        new { classname, type = isWall ? "i" : "s" }, transaction);

    // The entry with this classname in either section.
    public FurnidataEntry? Entry(string classname) => connection.QuerySingleOrDefault<FurnidataEntry>(
        $"SELECT {Columns} FROM furniture WHERE furnidata_classname = @classname", new { classname }, transaction);

    // The entry with this id, a floor entry before a wall entry.
    public FurnidataEntry? EntryById(int id) => connection.QueryFirstOrDefault<FurnidataEntry>(
        $"SELECT {Columns} FROM furniture WHERE furnidata_sprite_id = @id ORDER BY type = 'i' LIMIT 1", new { id }, transaction);

    public bool Any() => connection.QuerySingle<bool>("SELECT EXISTS (SELECT 1 FROM furniture WHERE has_furnidata)", transaction: transaction);

    // Writes the definition fields; the classname and id stay the row's.
    public void Save(FurnidataEntry entry) => connection.Execute("""
        UPDATE furniture SET revision = @Revision, category = @Category, default_dir = @DefaultDir, xdim = @XDim, ydim = @YDim,
        part_colors = @PartColors, name = @Name, description = @Description, ad_url = @AdUrl, excluded_dynamic = @ExcludedDynamic,
        custom_params = @CustomParams, special_type = @SpecialType, can_stand_on = @CanStandOn, can_sit_on = @CanSitOn,
        can_lay_on = @CanLayOn, can_put_stuff_on = @CanPutStuffOn, height = @Height, furni_line = @FurniLine, environment = @Environment,
        rare = @Rare, tradeable = @Tradeable, recyclable = @Recyclable WHERE id = @FurnitureId AND has_furnidata
        """, entry, transaction);
}
