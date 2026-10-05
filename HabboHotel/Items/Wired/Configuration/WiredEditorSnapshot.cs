using System.Collections.Immutable;
using System.Globalization;

namespace Plus.HabboHotel.Items.Wired.Configuration;

public sealed record WiredEditorSnapshot(
    uint ItemId, int SpriteId, WiredBoxDescriptor Descriptor, WiredConfiguration Configuration,
    int FurniLimit, ImmutableArray<int> BlockedItems)
{
    public static WiredEditorSnapshot Capture(IWiredConfiguredItem box) => Capture(box.Item, box.Descriptor,
        box is IWiredEditorConfigurationProvider editor ? editor.GetEditorConfiguration() : box.Configuration);

    public static WiredEditorSnapshot Capture(Item item, WiredBoxDescriptor descriptor, WiredConfiguration configuration,
        int furniLimit = WiredConfigurationLimits.SelectedItems, IReadOnlyList<int>? blockedItems = null) =>
        new(item.Id, item.Definition.SpriteId, descriptor, configuration, furniLimit,
            blockedItems?.ToImmutableArray() ?? []);

    public static WiredEditorSnapshot Trigger(IWiredItem box, IReadOnlyList<int> blockedItems) =>
        Legacy(box, WiredBoxCategory.Trigger, 5, box.StringData ?? string.Empty,
            box is IWiredCycle cycle ? [cycle.Delay] : [], 0, blockedItems);

    public static WiredEditorSnapshot Condition(IWiredItem box)
    {
        var text = box.StringData ?? string.Empty;
        var parameters = box.Type switch
        {
            WiredBoxType.ConditionMatchStateAndPosition or WiredBoxType.ConditionDontMatchStateAndPosition => Parse(text, "0;0;0", 3),
            WiredBoxType.ConditionUserCountInRoom or WiredBoxType.ConditionUserCountDoesntInRoom => Parse(text, "0;0", 2),
            WiredBoxType.ConditionFurniHasNoFurni => Parse(text, "0", 1),
            _ => ImmutableArray<int>.Empty
        };
        return Legacy(box, WiredBoxCategory.Condition, 5, text, parameters, 0, []);
    }

    public static WiredEditorSnapshot Effect(IWiredItem box, IReadOnlyList<int> blockedItems)
    {
        if (WiredLegacyCustomEditor.IsCustom(box))
        {
            if (!WiredLegacyCustomEditor.TryGetConfiguration(box, out var descriptor, out var configuration))
                throw new InvalidDataException("Invalid stored custom Wired settings.");
            return Capture(box.Item, descriptor, configuration, 0);
        }

        var text = box.StringData ?? string.Empty;
        ImmutableArray<int> parameters;
        switch (box.Type)
        {
            case WiredBoxType.EffectBotGivesHanditemBox:
                var handItem = Parts(text, "Bot name;0");
                text = handItem[0];
                parameters = [int.Parse(handItem[1], CultureInfo.InvariantCulture)];
                break;
            case WiredBoxType.EffectBotFollowsUserBox:
                var follows = Parts(text, "0;Bot name");
                text = follows[1];
                parameters = [int.Parse(follows[0], CultureInfo.InvariantCulture)];
                break;
            case WiredBoxType.EffectMatchPosition:
                parameters = Parse(text, "0;0;0", 3);
                break;
            case WiredBoxType.EffectMoveAndRotate:
                parameters = Parse(text, "0;0", 2);
                break;
            case WiredBoxType.EffectMuteTriggerer:
                parameters = Parse(text, "0;Message", 1);
                break;
            default:
                parameters = [];
                break;
        }
        var delay = box is IWiredCycle cycle && box.Type is not (WiredBoxType.EffectKickUser or WiredBoxType.EffectSetRollerSpeed)
            ? cycle.Delay : 0;
        return Legacy(box, WiredBoxCategory.Action, 15, text, parameters, delay, blockedItems);
    }

    private static WiredEditorSnapshot Legacy(IWiredItem box, WiredBoxCategory category, int furniLimit,
        string text, ImmutableArray<int> parameters, int delay, IReadOnlyList<int> blockedItems) =>
        Capture(box.Item, new(box.Type.ToString(), category, WiredBoxTypeUtility.GetWiredId(box.Type), 0, string.Empty),
            new WiredConfiguration
            {
                Text = text, IntParams = parameters, Delay = delay,
                SelectedItems = box.SetItems.Values.Select(item => item.Id).ToImmutableArray()
            }, furniLimit, blockedItems);

    private static string[] Parts(string text, string defaults) => (string.IsNullOrEmpty(text) ? defaults : text).Split(';');

    private static ImmutableArray<int> Parse(string text, string defaults, int count)
    {
        var parts = Parts(text, defaults);
        return Enumerable.Range(0, count).Select(index => int.Parse(parts[index], CultureInfo.InvariantCulture)).ToImmutableArray();
    }
}
