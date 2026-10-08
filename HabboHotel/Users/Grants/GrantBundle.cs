using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plus.HabboHotel.Users.Grants;

/// <summary>
/// A CMS reward bundle: signed credit and currency deltas, new furniture, badges and a rank. The wire form is standard
/// Base64 of UTF-8 JSON, because RCON parameters cannot carry ':'. Every bound is checked here, before any account is touched.
/// </summary>
public sealed class GrantBundle
{
    public const int MaxPayloadBytes = 8192;
    public const int MaxKeyLength = 64;
    public const int MaxCurrencies = 16;
    public const int MaxFurnitureEntries = 50;
    public const int MaxFurnitureAmount = 100;
    public const int MaxFurnitureItems = 500;
    public const int MaxBadges = 50;
    public const int MaxBadgeCodeLength = 35;
    public const int MaxRank = 7;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    public int Credits { get; private init; }
    public IReadOnlyList<KeyValuePair<int, int>> Currencies { get; private init; } = [];
    public IReadOnlyList<KeyValuePair<uint, int>> Furniture { get; private init; } = [];
    public IReadOnlyList<string> Badges { get; private init; } = [];
    public int? Rank { get; private init; }

    /// <summary>Hex SHA-256 of the decoded payload bytes; a retry must send the identical payload to replay.</summary>
    public string Sha256 { get; private init; } = string.Empty;

    public int FurnitureItems => Furniture.Sum(entry => entry.Value);

    /// <summary>Idempotency keys are opaque CMS ids from a small alphabet that cannot collide with RCON delimiters.</summary>
    public static bool ValidKey(string? key) => key is { Length: > 0 and <= MaxKeyLength } &&
        key.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');

    /// <summary>Decodes and bounds a payload; returns the failure code and a human detail, or null on success.</summary>
    public static GrantOutcome? TryParse(string? base64, out GrantBundle bundle)
    {
        bundle = new GrantBundle();

        if (string.IsNullOrEmpty(base64) || base64.Length > (MaxPayloadBytes + 2) / 3 * 4) {
            return GrantOutcome.Fail(base64?.Length > 0 ? GrantOutcome.TooLarge : GrantOutcome.InvalidPayload, "Payload is missing or larger than the cap.");
        }

        var bytes = new byte[base64.Length / 4 * 3 + 3];

        if (!Convert.TryFromBase64String(base64, bytes, out var length)) {
            return GrantOutcome.Fail(GrantOutcome.InvalidPayload, "Payload is not standard padded Base64.");
        }

        if (length > MaxPayloadBytes) {
            return GrantOutcome.Fail(GrantOutcome.TooLarge, "Payload is larger than the cap.");
        }

        Payload? payload;

        try {
            payload = JsonSerializer.Deserialize<Payload>(StrictUtf8.GetString(bytes, 0, length), JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException) {
            return GrantOutcome.Fail(GrantOutcome.InvalidPayload, "Payload is not a valid JSON bundle.");
        }

        if (payload == null) {
            return GrantOutcome.Fail(GrantOutcome.InvalidPayload, "Payload is not a JSON object.");
        }

        var currencies = new SortedDictionary<int, int>();

        if (payload.Currencies is { } types) {
            if (types.Count > MaxCurrencies) {
                return GrantOutcome.Fail(GrantOutcome.InvalidPayload, $"At most {MaxCurrencies} currencies.");
            }

            foreach (var (key, delta) in types) {
                if (!int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var type) ||
                    !ActivityPointType.IsValid(type) || type.ToString(System.Globalization.CultureInfo.InvariantCulture) != key) {
                    return GrantOutcome.Fail(GrantOutcome.InvalidPayload, $"Currency type '{key}' is not a non-negative integer.");
                }

                currencies[type] = delta;
            }
        }

        var furniture = new List<KeyValuePair<uint, int>>();

        if (payload.Furniture is { } entries) {
            if (entries.Length > MaxFurnitureEntries) {
                return GrantOutcome.Fail(GrantOutcome.InvalidPayload, $"At most {MaxFurnitureEntries} furniture entries.");
            }

            foreach (var entry in entries) {
                if (entry is not { BaseId: > 0, Amount: >= 1 and <= MaxFurnitureAmount }) {
                    return GrantOutcome.Fail(GrantOutcome.InvalidPayload, $"Furniture entries need baseId > 0 and amount 1..{MaxFurnitureAmount}.");
                }

                var index = furniture.FindIndex(existing => existing.Key == (uint)entry.BaseId);

                if (index < 0) {
                    furniture.Add(new((uint)entry.BaseId, entry.Amount));
                }
                else {
                    furniture[index] = new(furniture[index].Key, furniture[index].Value + entry.Amount);
                }
            }

            if (furniture.Sum(entry => entry.Value) > MaxFurnitureItems) {
                return GrantOutcome.Fail(GrantOutcome.InvalidPayload, $"At most {MaxFurnitureItems} furniture items.");
            }
        }

        var badges = new List<string>();

        if (payload.Badges is { } codes) {
            if (codes.Length > MaxBadges) {
                return GrantOutcome.Fail(GrantOutcome.InvalidPayload, $"At most {MaxBadges} badges.");
            }

            foreach (var code in codes) {
                if (code is not { Length: > 0 and <= MaxBadgeCodeLength } || code.Any(char.IsControl) || code.Trim() != code) {
                    return GrantOutcome.Fail(GrantOutcome.InvalidPayload, "Badge codes are 1..35 characters without surrounding whitespace.");
                }

                if (!badges.Contains(code, StringComparer.OrdinalIgnoreCase)) {
                    badges.Add(code);
                }
            }
        }

        if (payload.Rank is < 1 or > MaxRank) {
            return GrantOutcome.Fail(GrantOutcome.InvalidRank, $"Rank must be 1..{MaxRank} or null.");
        }

        bundle = new GrantBundle
        {
            Credits = payload.Credits ?? 0,
            Currencies = currencies.ToArray(),
            Furniture = furniture,
            Badges = badges,
            Rank = payload.Rank,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, length))),
        };

        return null;
    }

    private sealed class Payload
    {
        [JsonPropertyName("credits")]
        public int? Credits { get; init; }
        [JsonPropertyName("currencies")]
        public Dictionary<string, int>? Currencies { get; init; }
        [JsonPropertyName("furniture")]
        public FurnitureEntry?[]? Furniture { get; init; }
        [JsonPropertyName("badges")]
        public string?[]? Badges { get; init; }
        [JsonPropertyName("rank")]
        public int? Rank { get; init; }
    }

    private sealed class FurnitureEntry
    {
        [JsonPropertyName("baseId")]
        public int BaseId { get; init; }
        [JsonPropertyName("amount")]
        public int Amount { get; init; }
    }
}
