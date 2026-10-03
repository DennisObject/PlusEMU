using System.Text.Json;
using Plus.HabboHotel.Items.Editor;
using Xunit;

namespace Plus.Tests;

public class FurniEditorPayloadTests
{
    private static FurniEditorItem Item() => new()
    {
        Id = 5, SpriteId = 18, ItemName = "chair_polyfon", PublicName = "Dining Chair", Width = 1, Length = 1, StackHeight = 1, AllowSit = true,
        InteractionType = "default", InteractionModesCount = 1, AllowGift = true, AllowTrade = true, VendingIds = "0", EffectId = 0, Multiheight = "0"
    };

    // The editor sends its whole form (FurniEditorEditView EditField keys) on every save.
    private static Dictionary<string, object> Form() => new()
    {
        ["width"] = 1, ["length"] = 1, ["stackHeight"] = 1.0, ["allowStack"] = false, ["allowWalk"] = false, ["allowSit"] = true, ["allowLay"] = false,
        ["allowGift"] = true, ["allowTrade"] = true, ["allowRecycle"] = false, ["allowMarketplaceSell"] = false, ["allowInventoryStack"] = false,
        ["interactionType"] = "default", ["interactionModesCount"] = 1, ["customparams"] = "", ["vendingIds"] = "0", ["multiheight"] = "0",
        ["effectIdMale"] = 0, ["effectIdFemale"] = 0, ["clothingOnWalk"] = ""
    };

    private static (List<FurniEditorColumnChange> Changes, string? Error) Validate(Dictionary<string, object> form) =>
        FurniEditorUpdatePayload.Validate(JsonSerializer.Serialize(form), Item(), type => type is "default" or "gate" or "vendingmachine");

    [Fact]
    public void AnUnchangedFormChangesNothing()
    {
        var (changes, error) = Validate(Form());
        Assert.Null(error);
        Assert.Empty(changes);
    }

    [Fact]
    public void OnlyChangedFieldsBecomeColumnChanges()
    {
        var form = Form();
        form["width"] = 2;
        form["allowStack"] = true;
        form["stackHeight"] = 0.501;
        form["interactionType"] = "Gate ";
        form["vendingIds"] = "1, 2,3";
        form["effectIdMale"] = 4;
        form["effectIdFemale"] = 4;
        form["somethingNew"] = "ignored";

        var (changes, error) = Validate(form);

        Assert.Null(error);
        Assert.Equal(new (string, object)[] { ("width", 2), ("stack_height", 0.5), ("can_stack", "1"), ("interaction_type", "gate"), ("vending_ids", "1,2,3"), ("effect_id", 4) },
            changes.Select(change => (change.Column, change.Value)));
    }

    [Theory]
    [InlineData("width", 0)]
    [InlineData("width", 65)]
    [InlineData("stackHeight", 100.0)]
    [InlineData("interactionModesCount", -1)]
    [InlineData("interactionType", "not_a_handler")]
    [InlineData("vendingIds", "1,abc")]
    [InlineData("vendingIds", "-1")]
    [InlineData("multiheight", "0,1e9")]
    [InlineData("allowGift", "yes")]
    [InlineData("allowLay", true)]
    [InlineData("customparams", "x")]
    [InlineData("clothingOnWalk", "hat")]
    public void RefusesInvalidOrUnsupportedValues(string field, object value)
    {
        var form = Form();
        form[field] = value;
        var (changes, error) = Validate(form);
        Assert.NotNull(error);
        Assert.Empty(changes);
    }

    [Fact]
    public void RefusesDifferentEffectsPerGenderAndBadJson()
    {
        var form = Form();
        form["effectIdMale"] = 1;
        form["effectIdFemale"] = 2;
        Assert.NotNull(Validate(form).Error);
        Assert.NotNull(FurniEditorUpdatePayload.Validate("[1]", Item(), _ => true).Error);
        Assert.NotNull(FurniEditorUpdatePayload.Validate("{", Item(), _ => true).Error);
        Assert.NotNull(FurniEditorUpdatePayload.Validate(new string(' ', 9000), Item(), _ => true).Error);
    }

    [Fact]
    public void PublicNameIsBoundedAndPlain()
    {
        Assert.Equal("public_name", Validate(new() { ["publicName"] = "Chair" }).Changes.Single().Column);
        Assert.NotNull(Validate(new() { ["publicName"] = new string('a', 57) }).Error);
        Assert.NotNull(Validate(new() { ["publicName"] = "a\nb" }).Error);
    }

    [Fact]
    public void FurnidataPayloadAcceptsTextAndStructure()
    {
        var (payload, error) = FurnidataEditPayload.Parse("{\"name\":\"Chair\",\"structure\":{\"xdim\":2,\"canstandon\":true,\"height\":1.5}}");
        Assert.Null(error);
        Assert.Equal(("Chair", null), (payload!.Name, payload.Description));
        Assert.Equal(["canstandon", "height", "xdim"], payload.Structure.Keys.Order());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"name\":5}")]
    [InlineData("{\"name\":\"a\\u0000b\"}")]
    [InlineData("{\"structure\":{\"xdim\":0}}")]
    [InlineData("{\"structure\":{\"classname\":\"other\"}}")]
    [InlineData("{\"structure\":{\"canlayon\":1}}")]
    [InlineData("{\"structure\":[]}")]
    public void FurnidataPayloadRefusesInvalidInput(string json) => Assert.NotNull(FurnidataEditPayload.Parse(json).Error);
}
