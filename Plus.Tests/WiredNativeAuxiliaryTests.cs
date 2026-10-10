using System.Collections.Immutable;
using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredNativeAuxiliaryTests
{
    public static TheoryData<string> Names => new(
        "wf_act_give_var", "wf_act_remove_var", "wf_act_change_var_val", "wf_cnd_has_var", "wf_cnd_neg_has_var", "wf_cnd_var_val_match",
        "wf_cnd_var_age_match", "wf_trg_var_changed", "wf_xtra_anim_time", "wf_xtra_mov_no_animation", "wf_xtra_exec_in_order",
        "wf_xtra_unseen", "wf_xtra_execution_limit", "wf_xtra_random", "wf_xtra_or_eval", "wf_xtra_mov_carry_users",
        "wf_xtra_mov_physics", "wf_xtra_text_output_furni_name", "wf_xtra_text_output_username", "wf_xtra_filter_furni",
        "wf_xtra_filter_users", "wf_xtra_mov_curve", "wf_xtra_rotate_to_dir", "wf_xtra_text_input_variable",
        "wf_xtra_text_output_variable", "wf_xtra_filter_furni_by_var", "wf_xtra_filter_users_by_var", "wf_xtra_var_text_connector",
        "wf_xtra_var_time_util", "wf_xtra_var_lvlup_system", "wf_var_user", "wf_var_furni", "wf_var_room", "wf_var_context",
        "wf_var_echo", "wf_var_reference", "wf_act_give_currency", "wf_act_give_furni", "wf_act_init_transaction",
        "wf_act_cancel_transaction", "wf_cnd_chest_has_items", "wf_cnd_chest_has_item_type", "wf_trg_transaction_complete",
        "wf_trg_transaction_fail", "wf_xtra_scan_chest_furni_by_type", "wf_xtra_custom_contract", "wf_slc_furni_with_var",
        "wf_slc_users_with_var", "wf_xtra_var_fx_health", "wf_xtra_var_fx_progress", "wf_xtra_var_fx_level", "wf_xtra_var_fx_status",
        "wf_xtra_var_fx_boss", "wf_xtra_var_fx_number");

    [Theory]
    [MemberData(nameof(Names))]
    public void DefaultsSurviveTheStoredJsonAndBindAsTheirOwnRuntime(string name)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
        Assert.True(WiredNativeEditorProjection.Supports(name));
        var native = WiredNativeEditorProjection.DefaultNative(descriptor);
        var stored = JsonSerializer.Deserialize<WiredNativeEditorConfiguration>(JsonSerializer.Serialize(native))!;
        Assert.True(WiredNativeEditorProjection.TryCompile(7, descriptor, stored, out var runtime));
        Assert.True(WiredNativeEditorProjection.IsBound(7, descriptor, runtime));
        Assert.Equal(WiredNativeEditorProjection.Metadata(name).VariableDefaults.Length, runtime.VariableIds.Length);
    }

    private static WiredConfiguration Compile(string name, WiredNativeEditorConfiguration native)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
        var merged = native with { Category = descriptor.Category, NativeCode = descriptor.EditorCode };
        Assert.True(WiredNativeEditorProjection.TryCompile(7, descriptor, merged, out var runtime));

        return runtime;
    }

    private static WiredNativeEditorConfiguration Native(string name, int[] owned, int[]? furni = null, int[]? users = null,
        string[]? variables = null, string text = "")
    {
        WiredBoxRegistry.TryGet(name, out var descriptor);
        var defaults = WiredNativeEditorProjection.DefaultNative(descriptor);

        return defaults with
        {
            OwnedIntParams = [.. owned],
            FurniSourceTypes = furni is null ? defaults.FurniSourceTypes : [.. furni],
            UserSourceTypes = users is null ? defaults.UserSourceTypes : [.. users],
            VariableIds = variables is null ? defaults.VariableIds : [.. variables],
            Text = text
        };
    }

    [Fact]
    public void ScalarBoxesKeepTheOpaqueIdsAndTranslateTargetsLiteralsAndComparisons()
    {
        // AIR user target 1 -> scalar user 0; a literal above 32 bits rides the extension pair.
        var give = Compile("wf_act_give_var", Native("wf_act_give_var", [1, 0, 7, 1], [100], [0], ["user:12"]));
        Assert.Equal([0, 1, 7, 0, 100], give.IntParams.ToArray());
        Assert.Equal(["user:12"], give.VariableIds.ToArray());
        var wide = Compile("wf_act_give_var", Native("wf_act_give_var", [0, 1, 5, 0], [0], [0], ["furni:3"]));
        Assert.Equal([1, 0, 5, 0, 0, 1, 1], wide.IntParams.ToArray());
        var change = Compile("wf_act_change_var_val", Native("wf_act_change_var_val", [0, 2, 1, 0, 0, 1], [0, 101], [0, 200], ["furni:3", "user:4"]));
        Assert.Equal([1, 2, 1, 0, 0, 0, 0, 200, 101], change.IntParams.ToArray());
        // AIR '>' is code 2; the executors use 0 for greater-than.
        var match = Compile("wf_cnd_var_val_match", Native("wf_cnd_var_val_match", [1, 2, 0, 0, 9, 1], variables: ["user:12", "n"]) with { Quantifier = 1 });
        Assert.Equal(0, match.IntParams[1]);
        Assert.Equal(9, match.IntParams[3]);
        Assert.Equal(1, match.IntParams[^1]);
        // The age form lists the comparison before the clock; the executors take the clock first.
        var age = Compile("wf_cnd_var_age_match", Native("wf_cnd_var_age_match", [1, 2, 1, 0, 30, 2], variables: ["user:12"]));
        Assert.Equal([0, 1, 2, 30, 2, 0, 0, 0], age.IntParams.ToArray());
    }

    [Fact]
    public void VariableChangedTakesItsTargetFromThePickedVariableAndMapsTheMasks()
    {
        var changed = Compile("wf_trg_var_changed", Native("wf_trg_var_changed", [1, 1, 0, 5, -1], variables: ["furni:5"]));
        Assert.Equal([1, 1, 1, 1, 0, 1, 0, -1], changed.IntParams.ToArray());
    }

    [Fact]
    public void AddonFieldsFollowTheAirFormOrder()
    {
        // The form lists skipped executions before the amount; the picker wants the amount first.
        Assert.Equal([2, 3], Compile("wf_xtra_random", Native("wf_xtra_random", [3, 2])).IntParams.ToArray());
        Assert.Equal([3, 2000], Compile("wf_xtra_execution_limit", Native("wf_xtra_execution_limit", [3, 4])).IntParams.ToArray());
        Assert.Equal([5, 0, 7], Compile("wf_xtra_or_eval", Native("wf_xtra_or_eval", [-1, 1, 7])).IntParams.ToArray());
        Assert.Equal([2, 0, 1], Compile("wf_xtra_or_eval", Native("wf_xtra_or_eval", [2, 0, 0])).IntParams.ToArray());
        var carry = Compile("wf_xtra_mov_carry_users", Native("wf_xtra_mov_carry_users", [1], users: [200]));
        Assert.Equal([1, 200], carry.IntParams.ToArray());
        var names = Compile("wf_xtra_text_output_username", Native("wf_xtra_text_output_username", [1], users: [0], text: "who\t; "));
        Assert.Equal([2, 0], names.IntParams.ToArray());
        Assert.Equal("who\t; ", names.Text);
    }

    [Fact]
    public void DefinitionFormsMapToTheDefinitionDecoder()
    {
        // The user form lists availability first.
        var user = Compile("wf_var_user", Native("wf_var_user", [10, 1], text: "score"));
        Assert.Equal([1, 10], user.IntParams.ToArray());
        Assert.True(WiredVariableDefinitions.TryDecode("wf_var_user", 7, 1, 5, user, out var definition, out _));
        Assert.Equal(WiredVariableAvailability.Persistent, definition!.Availability);
        Assert.True(definition.HasValue);
        var echo = Compile("wf_var_echo", Native("wf_var_echo", [], variables: ["furni:9"], text: "mirror"));
        Assert.True(WiredVariableDefinitions.TryDecode("wf_var_echo", 7, 1, 5, echo, out var echoed, out _));
        Assert.Equal(new WiredVariableReference(WiredVariableTarget.Furni, "custom:9"), echoed!.Link!.Source);
        var reference = Compile("wf_var_reference", Native("wf_var_reference", [1], variables: ["shared:2:room:4"], text: "far"));
        Assert.True(WiredVariableDefinitions.TryDecode("wf_var_reference", 7, 1, 5, reference, out var linked, out _));
        Assert.Equal(2u, linked!.Link!.SourceRoomId);
        Assert.True(linked.Link.ReadOnly);
    }

    [Fact]
    public void ChestFormsKeepTheirFieldOrderAndExposeSourcesByIndex()
    {
        var give = Compile("wf_act_give_currency", Native("wf_act_give_currency", [0, 5, 1, 0, 1, 13], [100, 200], [11, 0], ["furni:2"], "well done"));
        Assert.Equal([0, 5, 1, 1, 1, 13], give.IntParams.ToArray());
        Assert.Equal(200, give.FurniSources["f1"]);
        Assert.Equal(11, give.UserSources["u0"]);
        Assert.Equal("well done", give.Text);
        var has = Compile("wf_cnd_chest_has_item_type", Native("wf_cnd_chest_has_item_type", [3, 0, -10, 2], [100, 100, 0], [0], ["n"]));
        Assert.Equal([3, 0, 3, 2], has.IntParams.ToArray());
        Assert.Equal(["n"], has.VariableIds.ToArray());
    }

    [Fact]
    public void WithVariableSelectorsMapComparisonValueModeAndFlags()
    {
        var selector = Compile("wf_slc_furni_with_var", Native("wf_slc_furni_with_var", [2, 2, 0, 0, 1], variables: ["furni:6", "user:7"]) with { Filter = true, Inverse = false });
        Assert.Equal([1, 0, 1, 0, 0, 0, 0, 1, 0], selector.IntParams.ToArray());
        Assert.Equal(["furni:6", "user:7"], selector.VariableIds.ToArray());
    }

    [Fact]
    public void SelectorWriterEmitsItsFlagsAndTheVariableContexts()
    {
        Assert.True(WiredBoxRegistry.TryGet("wf_slc_furni_with_var", out var descriptor));
        var native = WiredNativeEditorProjection.DefaultNative(descriptor) with { Filter = true, Inverse = false };
        Assert.True(WiredNativeEditorProjection.TryCompile(7, descriptor, native, out var runtime));
        var shared = new WiredNativeSharedVariable(5, "Far room", new("shared:5:room:9", 0, "total", 11, -10, false, false, true, false, false, false, true, true, null));
        var packet = new RecordingPacket();
        WiredLegacyProtocol.Write(packet, 7, 100, descriptor, runtime, 20, [], native, 77, [shared]);
        var values = packet.Values;
        var code = values.IndexOf(descriptor.EditorCode);
        // The flags follow the code, then the advanced footer starts with its own true.
        Assert.Equal([true, false, true], values.Skip(code + 1).Take(3).Cast<bool>());
        var at = Enumerable.Range(0, values.Count - 3).First(i => values[i] is 2 && values[i + 1] is 0 && values[i + 2] is 77 && values[i + 3] is 4);
        Assert.Equal([2, 0, 77, 4, 1, 5, "Far room", "shared:5:room:9", 0, "total", 11, -10], values.Skip(at).Take(12).ToArray());
    }

    private sealed class RecordingPacket : Plus.HabboHotel.GameClients.IOutgoingPacket
    {
        public List<object> Values { get; } = [];
        public int MessageId { get; set; }
        public ReadOnlyMemory<byte> Buffer => default;
        public void WriteByte(byte value) => Values.Add(value);
        public void WriteShort(short value) => Values.Add(value);
        public void WriteInt(int value) => Values.Add(value);
        public void WriteInteger(int value) => Values.Add(value);
        public void WriteUInt(uint value) => Values.Add(value);
        public void WriteUInteger(uint value) => Values.Add(value);
        public void WriteBool(bool value) => Values.Add(value);
        public void WriteBoolean(bool value) => Values.Add(value);
        public void WriteString(string value) => Values.Add(value);
        public void WriteDouble(double value) => Values.Add(value);
    }

    [Fact]
    public void CatalogIdsResolveOnlyThroughTheModulesCurrentAuthority()
    {
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), TimeProvider.System);
        Assert.True(module.TryResolveCatalogId("furni:10", WiredVariableTarget.Furni, out var reference));
        Assert.Equal("custom:10", reference.Token);
        Assert.False(module.TryResolveCatalogId("user:10", WiredVariableTarget.Furni, out _));
        Assert.False(module.TryResolveCatalogId("furni:11", WiredVariableTarget.Furni, out _));
        Assert.False(module.TryResolveCatalogId("n", WiredVariableTarget.Furni, out _));
    }

    private sealed class Directory : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => id == 10 ? new(10, 1, 5, "tag", WiredVariableTarget.Furni, WiredVariableAvailability.RoomActive, true) : null;
        public uint? GetRoomOwner(uint roomId) => 5;
    }
}
