using System.Text;
using System.Text.Json;
using Plus.HabboHotel.Users.Grants;
using Xunit;

namespace Plus.Tests;

public sealed class GrantBundleTests
{
    [Fact]
    public void ABundleIsNormalizedAndHashedOverItsExactBytes()
    {
        var json = "{\"credits\":-5,\"currencies\":{\"103\":2,\"0\":1},\"furniture\":[{\"baseId\":9,\"amount\":2},{\"baseId\":9,\"amount\":3}],\"badges\":[\"ACH\",\"ach\"],\"rank\":null}";

        Assert.Null(GrantBundle.TryParse(Encode(json), out var bundle));

        Assert.Equal(-5, bundle.Credits);
        Assert.Equal([new(0, 1), new(103, 2)], bundle.Currencies);
        Assert.Equal([new(9u, 5)], bundle.Furniture);
        Assert.Equal(["ACH"], bundle.Badges);
        Assert.Null(bundle.Rank);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json))), bundle.Sha256);
        Assert.Null(GrantBundle.TryParse(Encode("{}"), out var empty));
        Assert.Empty(empty.Furniture);
    }

    [Theory]
    [InlineData("{\"credits\":1,\"extra\":1}", GrantOutcome.InvalidPayload)]
    [InlineData("{\"credits\":1,\"credits\":2}", GrantOutcome.InvalidPayload)]
    [InlineData("{\"credits\":1.5}", GrantOutcome.InvalidPayload)]
    [InlineData("{\"credits\":2147483648}", GrantOutcome.InvalidPayload)]
    [InlineData("{\"currencies\":{\"-1\":1}}", GrantOutcome.InvalidPayload)]
    [InlineData("{\"currencies\":{\"05\":1}}", GrantOutcome.InvalidPayload)]
    [InlineData("{\"furniture\":[{\"baseId\":9}]}", GrantOutcome.InvalidPayload)]
    [InlineData("{\"furniture\":[{\"baseId\":9,\"amount\":101}]}", GrantOutcome.InvalidPayload)]
    [InlineData("{\"furniture\":[{\"baseId\":0,\"amount\":1}]}", GrantOutcome.InvalidPayload)]
    [InlineData("{\"badges\":[\"\"]}", GrantOutcome.InvalidPayload)]
    [InlineData("{\"badges\":[\" ACH\"]}", GrantOutcome.InvalidPayload)]
    [InlineData("{\"rank\":0}", GrantOutcome.InvalidRank)]
    [InlineData("{\"rank\":8}", GrantOutcome.InvalidRank)]
    [InlineData("[]", GrantOutcome.InvalidPayload)]
    [InlineData("null", GrantOutcome.InvalidPayload)]
    public void InvalidBundlesAreRefusedWithTheirId(string json, string code)
    {
        Assert.Equal(code, GrantBundle.TryParse(Encode(json), out _)?.Code);
    }

    [Fact]
    public void BoundsAndEncodingAreEnforced()
    {
        var furniture = Enumerable.Range(1, 6).Select(id => new { baseId = id, amount = 100 }).ToArray();

        Assert.Equal(GrantOutcome.InvalidPayload, GrantBundle.TryParse(Encode(JsonSerializer.Serialize(new { furniture })), out _)?.Code);
        Assert.Equal(GrantOutcome.InvalidPayload, GrantBundle.TryParse(Encode(JsonSerializer.Serialize(new { badges = Enumerable.Range(0, 51).Select(i => $"B{i}") })), out _)?.Code);
        Assert.Equal(GrantOutcome.TooLarge, GrantBundle.TryParse(Encode("{\"badges\":[\"" + new string('A', GrantBundle.MaxPayloadBytes) + "\"]}"), out _)?.Code);
        Assert.Equal(GrantOutcome.InvalidPayload, GrantBundle.TryParse("not base64!", out _)?.Code);
        Assert.Equal(GrantOutcome.InvalidPayload, GrantBundle.TryParse(Convert.ToBase64String([0xff, 0xfe]), out _)?.Code);
        Assert.Equal(GrantOutcome.InvalidPayload, GrantBundle.TryParse("", out _)?.Code);
    }

    [Theory]
    [InlineData("order-1", true)]
    [InlineData("A.b_C-9", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("colon:key", false)]
    public void KeysUseASmallAlphabet(string key, bool valid)
    {
        Assert.Equal(valid, GrantBundle.ValidKey(key));
        Assert.False(GrantBundle.ValidKey(new string('k', GrantBundle.MaxKeyLength + 1)));
    }

    private static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
}
