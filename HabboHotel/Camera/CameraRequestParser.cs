using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Plus.HabboHotel.Camera;

public enum CameraChannel
{
    Photo,
    Thumbnail
}

internal enum CameraParseStatus
{
    Accepted,
    Rejected,
    Malformed
}

internal enum CameraRejectReason
{
    None,
    UnknownProperty,
    Pixels,
    Url,
    NonFinite,
    Crop,
    Duplicate,
    Effect,
    Guid,
    Oversized,
    Trailing,
    Schema
}

internal sealed record CameraParseResult(
    CameraParseStatus Status,
    CameraRejectReason Reason,
    string RequestId,
    string Stage,
    CameraCommand? Command,
    string Action = "");

internal abstract record CameraCommand
{
    internal sealed record Capture(string RequestId, CameraViewport Viewport) : CameraCommand;

    internal sealed record Render(string RequestId, string DraftId, IReadOnlyList<CameraEffectSelection> Effects, bool Zoom) : CameraCommand;

    internal sealed record Delete(string DraftId) : CameraCommand;
}

internal sealed record CameraViewport(
    int Width,
    int Height,
    double OffsetX,
    double OffsetY,
    double X,
    double Y,
    int CropWidth,
    int CropHeight,
    double Scale,
    double LocationX,
    double LocationY,
    double LocationZ);

internal sealed record CameraEffectSelection(string Name, double Strength);

internal static class CameraRequestParser
{
    public const int MaxJsonBytes = 8192;
    public const int PhotoCrop = 320;
    public const int ThumbnailCrop = 110;

    private static readonly Regex GuidText = new(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> PixelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "pixels", "pixel", "bitmap", "png", "image", "sprite", "figure", "tilemap"
    };

    private static readonly HashSet<string> UrlKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "url", "imageurl", "clickurl", "href", "src"
    };

    public static CameraParseResult Parse(CameraRequestPayload payload, CameraChannel channel, IReadOnlyDictionary<string, int> catalogue, int photoLevel)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(catalogue);

        if (payload.FrameError != CameraRejectReason.None) {
            return Malformed(payload.FrameError);
        }

        if (payload.Json == null) {
            return Malformed(CameraRejectReason.Schema);
        }

        if (Encoding.UTF8.GetByteCount(payload.Json) > MaxJsonBytes) {
            return Malformed(CameraRejectReason.Oversized);
        }

        return ParseJson(payload.Json, channel, catalogue, photoLevel);
    }

    // One JSON value and nothing after it, or null.
    private static JsonNode? ReadRoot(string json)
    {
        try {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });

            if (!reader.Read()) {
                return null;
            }

            var root = ReadNode(ref reader);

            return reader.Read() ? null : root;
        }
        catch (JsonException) {
            return null;
        }
    }

    private static CameraParseResult ParseJson(string json, CameraChannel channel, IReadOnlyDictionary<string, int> catalogue, int photoLevel)
    {
        var root = ReadRoot(json);

        if (root is not JsonObject body) {
            return Malformed(CameraRejectReason.Schema);
        }

        var requestId = StringValue(body, "requestId");
        var action = StringValue(body, "action");
        var stage = action switch
        {
            "capture" => "capture",
            "render" => "render",
            _ => ""
        };
        bool trustworthy = stage.Length > 0 && requestId != null && GuidText.IsMatch(requestId);
        var knownAction = action ?? "";
        var hazard = Hazard(root);

        if (hazard != null) {
            return Fail(hazard.Value, requestId, stage, trustworthy, knownAction);
        }

        if (HasDuplicate(root)) {
            return Fail(CameraRejectReason.Duplicate, requestId, stage, trustworthy, knownAction);
        }

        if (action is not ("capture" or "render" or "delete")) {
            return Malformed(CameraRejectReason.Schema);
        }

        if (channel == CameraChannel.Thumbnail && action != "capture") {
            return Fail(CameraRejectReason.Schema, requestId, stage, trustworthy, knownAction);
        }

        if (action != "delete" && !trustworthy) {
            return Malformed(CameraRejectReason.Guid, knownAction);
        }

        var version = RequireInteger(body, "v", out var versionReason);

        if (versionReason != null) {
            return Fail(versionReason.Value, requestId, stage, trustworthy, knownAction);
        }

        if (version != 1) {
            return Fail(CameraRejectReason.Schema, requestId, stage, trustworthy, knownAction);
        }

        return action switch
        {
            "capture" => ParseCapture(body, channel, requestId!, stage),
            "render" => ParseRender(body, requestId!, stage, catalogue, photoLevel),
            _ => ParseDelete(body)
        };
    }

    private static CameraParseResult ParseCapture(JsonObject body, CameraChannel channel, string requestId, string stage)
    {
        var unexpected = Unexpected(body, "v", "action", "requestId", "viewport");

        if (unexpected != null) {
            return Fail(unexpected.Value, requestId, stage, true);
        }

        if (body.Props["viewport"] is not JsonObject viewport) {
            return Fail(CameraRejectReason.Schema, requestId, stage, true);
        }

        var read = ReadViewport(viewport, channel == CameraChannel.Photo ? PhotoCrop : ThumbnailCrop, out var viewportReason);

        if (read == null) {
            return Fail(viewportReason!.Value, requestId, stage, true);
        }

        var command = new CameraCommand.Capture(requestId, read);

        return new CameraParseResult(CameraParseStatus.Accepted, CameraRejectReason.None, requestId, stage, command);
    }

    // The camera's viewport as it opens, so the room can be prepared as it will be photographed. Null unless it is a
    // photo viewport by the same rules as a capture.
    public static CameraViewport? ParseViewport(string? json)
    {
        if (json == null || Encoding.UTF8.GetByteCount(json) > MaxJsonBytes || ReadRoot(json) is not JsonObject viewport ||
            Hazard(viewport) != null || HasDuplicate(viewport)) {
            return null;
        }

        return ReadViewport(viewport, PhotoCrop, out _);
    }

    private static CameraViewport? ReadViewport(JsonObject viewport, int crop, out CameraRejectReason? reason)
    {
        reason = null;

        var unexpected = Unexpected(viewport, "width", "height", "offsetX", "offsetY", "x", "y", "cropWidth", "cropHeight", "scale", "locationX", "locationY", "locationZ");

        if (unexpected != null) {
            reason = unexpected;

            return null;
        }

        var width = ReadInt(viewport, "width", 320, 2048, CameraRejectReason.Schema, out var widthReason);

        if (widthReason != null) {
            reason = widthReason;

            return null;
        }

        var height = ReadInt(viewport, "height", 320, 2048, CameraRejectReason.Schema, out var heightReason);

        if (heightReason != null) {
            reason = heightReason;

            return null;
        }

        var offsetX = ReadFinite(viewport, "offsetX", -4096, 4096, out var offsetXReason);

        if (offsetXReason != null) {
            reason = offsetXReason;

            return null;
        }

        var offsetY = ReadFinite(viewport, "offsetY", -4096, 4096, out var offsetYReason);

        if (offsetYReason != null) {
            reason = offsetYReason;

            return null;
        }

        var x = ReadFinite(viewport, "x", -4096, 4096, out var xReason);

        if (xReason != null) {
            reason = xReason;

            return null;
        }

        var y = ReadFinite(viewport, "y", -4096, 4096, out var yReason);

        if (yReason != null) {
            reason = yReason;

            return null;
        }

        var cropWidth = ReadInt(viewport, "cropWidth", crop, crop, CameraRejectReason.Crop, out var cropWidthReason);

        if (cropWidthReason != null) {
            reason = cropWidthReason;

            return null;
        }

        var cropHeight = ReadInt(viewport, "cropHeight", crop, crop, CameraRejectReason.Crop, out var cropHeightReason);

        if (cropHeightReason != null) {
            reason = cropHeightReason;

            return null;
        }

        var scale = ReadFinite(viewport, "scale", 1, 1, out var scaleReason);

        if (scaleReason != null) {
            reason = scaleReason;

            return null;
        }

        var locationX = ReadFinite(viewport, "locationX", -256, 256, out var locationXReason);

        if (locationXReason != null) {
            reason = locationXReason;

            return null;
        }

        var locationY = ReadFinite(viewport, "locationY", -256, 256, out var locationYReason);

        if (locationYReason != null) {
            reason = locationYReason;

            return null;
        }

        var locationZ = ReadFinite(viewport, "locationZ", -256, 256, out var locationZReason);

        if (locationZReason != null) {
            reason = locationZReason;

            return null;
        }

        return new CameraViewport(width, height, offsetX, offsetY, x, y, cropWidth, cropHeight, scale, locationX, locationY, locationZ);
    }

    private static CameraParseResult ParseRender(JsonObject body, string requestId, string stage, IReadOnlyDictionary<string, int> catalogue, int photoLevel)
    {
        var unexpected = Unexpected(body, "v", "action", "requestId", "draftId", "effects", "zoom");

        if (unexpected != null) {
            return Fail(unexpected.Value, requestId, stage, true);
        }

        var draftId = StringValue(body, "draftId");

        if (draftId == null || !GuidText.IsMatch(draftId)) {
            return Fail(CameraRejectReason.Guid, requestId, stage, true);
        }

        if (body.Props["zoom"] is not JsonBool zoom) {
            return Fail(CameraRejectReason.Schema, requestId, stage, true);
        }

        if (body.Props["effects"] is not JsonArray effects) {
            return Fail(CameraRejectReason.Schema, requestId, stage, true);
        }

        if (effects.Items.Count > 8) {
            return Fail(CameraRejectReason.Effect, requestId, stage, true);
        }

        var selected = new List<CameraEffectSelection>(effects.Items.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in effects.Items) {
            if (item is not JsonObject effect) {
                return Fail(CameraRejectReason.Effect, requestId, stage, true);
            }

            unexpected = Unexpected(effect, "name", "strength");

            if (unexpected != null) {
                return Fail(unexpected.Value, requestId, stage, true);
            }

            var name = StringValue(effect, "name");

            if (name == null || !catalogue.TryGetValue(name, out var minLevel)) {
                return Fail(CameraRejectReason.Effect, requestId, stage, true);
            }

            if (!names.Add(name)) {
                return Fail(CameraRejectReason.Duplicate, requestId, stage, true);
            }

            if (minLevel > photoLevel) {
                return Fail(CameraRejectReason.Effect, requestId, stage, true);
            }

            var strength = ReadFinite(effect, "strength", 0, 1, out var strengthReason);

            if (strengthReason == CameraRejectReason.NonFinite) {
                return Fail(CameraRejectReason.NonFinite, requestId, stage, true);
            }

            if (strengthReason != null) {
                return Fail(CameraRejectReason.Effect, requestId, stage, true);
            }

            selected.Add(new CameraEffectSelection(name, strength));
        }

        return new CameraParseResult(CameraParseStatus.Accepted, CameraRejectReason.None, requestId, stage,
            new CameraCommand.Render(requestId, draftId, selected, zoom.Value));
    }

    private static CameraParseResult ParseDelete(JsonObject body)
    {
        if (Unexpected(body, "v", "action", "draftId") != null) {
            return Malformed(CameraRejectReason.UnknownProperty, "delete");
        }

        var draftId = StringValue(body, "draftId");

        if (draftId == null || !GuidText.IsMatch(draftId)) {
            return Malformed(CameraRejectReason.Guid, "delete");
        }

        return new CameraParseResult(CameraParseStatus.Accepted, CameraRejectReason.None, "", "", new CameraCommand.Delete(draftId), "delete");
    }

    private static CameraParseResult Fail(CameraRejectReason reason, string? requestId, string stage, bool trustworthy, string action = "")
    {
        if (trustworthy) {
            return new CameraParseResult(CameraParseStatus.Rejected, reason, requestId ?? "", stage, null, action);
        }

        return Malformed(reason, action);
    }

    private static CameraParseResult Malformed(CameraRejectReason reason, string action = "") =>
        new(CameraParseStatus.Malformed, reason, "", "", null, action);

    private static CameraRejectReason? Unexpected(JsonObject body, params string[] allowed)
    {
        var permit = new HashSet<string>(allowed, StringComparer.Ordinal);

        foreach (var key in body.Props.Keys) {
            if (!permit.Contains(key)) {
                return PixelKeys.Contains(key) ? CameraRejectReason.Pixels : UrlKeys.Contains(key) ? CameraRejectReason.Url : CameraRejectReason.UnknownProperty;
            }
        }

        foreach (var key in allowed) {
            if (!body.Props.ContainsKey(key)) {
                return CameraRejectReason.Schema;
            }
        }

        return null;
    }

    private static int ReadInt(JsonObject body, string name, int min, int max, CameraRejectReason outOfRange, out CameraRejectReason? reason)
    {
        reason = null;

        if (!body.Props.TryGetValue(name, out var node) || node is not JsonNumber number) {
            reason = CameraRejectReason.Schema;

            return 0;
        }

        if (!double.TryParse(number.Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
            reason = CameraRejectReason.Schema;

            return 0;
        }

        if (!double.IsFinite(parsed)) {
            reason = CameraRejectReason.NonFinite;

            return 0;
        }

        if (!IsPlainInteger(number.Raw) || !int.TryParse(number.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) {
            reason = outOfRange;

            return 0;
        }

        if (value < min || value > max) {
            reason = outOfRange;
        }

        return value;
    }

    private static int RequireInteger(JsonObject body, string name, out CameraRejectReason? reason)
    {
        var value = ReadInt(body, name, 1, 1, CameraRejectReason.Schema, out reason);

        return value;
    }

    private static double ReadFinite(JsonObject body, string name, double min, double max, out CameraRejectReason? reason)
    {
        reason = null;

        if (!body.Props.TryGetValue(name, out var node) || node is not JsonNumber number) {
            reason = CameraRejectReason.Schema;

            return 0;
        }

        if (!double.TryParse(number.Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
            reason = CameraRejectReason.Schema;

            return 0;
        }

        if (!double.IsFinite(parsed)) {
            reason = CameraRejectReason.NonFinite;

            return 0;
        }

        if (parsed < min || parsed > max) {
            reason = CameraRejectReason.Schema;

            return 0;
        }

        return parsed;
    }

    private static bool IsPlainInteger(string raw) => Regex.IsMatch(raw, "^-?[0-9]+$");

    private static string? StringValue(JsonObject body, string name) =>
        body.Props.TryGetValue(name, out var node) && node is JsonString text ? text.Value : null;

    private static bool HasDuplicate(JsonNode node) => node switch
    {
        JsonObject obj => obj.Duplicate || obj.Props.Values.Any(HasDuplicate),
        JsonArray array => array.Items.Any(HasDuplicate),
        _ => false
    };

    private static CameraRejectReason? Hazard(JsonNode node)
    {
        switch (node) {
            case JsonString text when IsPixels(text.Value):
                return CameraRejectReason.Pixels;
            case JsonString text when IsUrl(text.Value):
                return CameraRejectReason.Url;
            case JsonObject obj:
                foreach (var (key, value) in obj.Props) {
                    if (PixelKeys.Contains(key) || IsPixels(key)) {
                        return CameraRejectReason.Pixels;
                    }

                    if (UrlKeys.Contains(key) || IsUrl(key)) {
                        return CameraRejectReason.Url;
                    }

                    var child = Hazard(value);

                    if (child != null) {
                        return child;
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array.Items) {
                    var child = Hazard(item);

                    if (child != null) {
                        return child;
                    }
                }

                break;
        }

        return null;
    }

    private static bool IsUrl(string value) =>
        value.Contains("://", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("data:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("file:", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("//", StringComparison.Ordinal);

    private static bool IsPixels(string value) =>
        value.Contains("iVBORw0KGgo", StringComparison.Ordinal) || value.Contains("\u0089PNG", StringComparison.Ordinal);

    private static JsonNode ReadNode(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType) {
            case JsonTokenType.String:
                return new JsonString(reader.GetString() ?? "");
            case JsonTokenType.Number:
                return new JsonNumber(Encoding.UTF8.GetString(reader.ValueSpan));
            case JsonTokenType.True:
                return new JsonBool(true);
            case JsonTokenType.False:
                return new JsonBool(false);
            case JsonTokenType.Null:
                return new JsonNull();
            case JsonTokenType.StartObject:
                var obj = new JsonObject();

                while (reader.Read()) {
                    if (reader.TokenType == JsonTokenType.EndObject) {
                        return obj;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName) {
                        throw new JsonException();
                    }

                    var name = reader.GetString() ?? "";

                    if (!reader.Read()) {
                        throw new JsonException();
                    }

                    if (!obj.Props.TryAdd(name, ReadNode(ref reader))) {
                        obj.Duplicate = true;
                    }
                }

                throw new JsonException();
            case JsonTokenType.StartArray:
                var array = new JsonArray();

                while (reader.Read()) {
                    if (reader.TokenType == JsonTokenType.EndArray) {
                        return array;
                    }

                    array.Items.Add(ReadNode(ref reader));
                }

                throw new JsonException();
            default:
                throw new JsonException();
        }
    }

    private abstract record JsonNode;

    private sealed record JsonString(string Value) : JsonNode;

    private sealed record JsonNumber(string Raw) : JsonNode;

    private sealed record JsonBool(bool Value) : JsonNode;

    private sealed record JsonNull : JsonNode;

    private sealed record JsonArray : JsonNode
    {
        public List<JsonNode> Items { get; } = new();
    }

    private sealed record JsonObject : JsonNode
    {
        public Dictionary<string, JsonNode> Props { get; } = new(StringComparer.Ordinal);
        public bool Duplicate { get; set; }
    }
}
