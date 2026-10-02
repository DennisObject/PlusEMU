using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.HabboHotel.Items.Wired.Variables;

public sealed partial class WiredRoomVariables
{
    private readonly Dictionary<uint, WiredVariableTextInputBox> _textInputs = [];

    /// <summary>Engine pre-predicate hook: null uses the ordinary speech predicate; false rejects without publishing captures.</summary>
    public bool? CaptureSpeech(WiredRuntimeContext context, IWiredContextualTrigger trigger)
    {
        if (trigger.Descriptor.CanonicalName != "wf_trg_says_something" || !ReferenceEquals(context.Room, _room)) return null;
        var boxes = _textInputs.Values.Where(box => IsAttached(box.Item) && box.Item.GetX == trigger.Item.GetX && box.Item.GetY == trigger.Item.GetY)
            .OrderBy(box => box.Item.GetZ).ThenBy(box => box.Item.Id).ToArray();
        if (boxes.Length == 0) return null;
        var triggerConfig = context.ConfigurationOf(trigger);
        if (!trigger.TryValidateConfiguration(triggerConfig, out triggerConfig, out _) || context.Event.Kind != WiredEventKind.Speech || context.Event.Actor is null || triggerConfig.IntParams.Length != 3
            || triggerConfig.IntParams[2] == 1 && context.Event.Actor.HabboId != _room.OwnerId) return false;
        var capturers = new List<WiredVariableCapturer>();
        foreach (var box in boxes)
        {
            if (!box.TryValidateConfiguration(context.ConfigurationOf(box), out var config, out _)) continue;
            var parts = config.Text.Split('\t'); WiredVariableModule.TryDefinitionId(parts[0], out var id);
            capturers.Add(new(id, parts[1], config.IntParams[0] == 2 ? MetadataOn(id, "wf_xtra_var_text_connector")?.TextConnector : null));
        }
        if (capturers.Count == 0) return null;
        if (!WiredVariableTextCapture.TryMatch(triggerConfig.Text, context.Event.Message, triggerConfig.IntParams[0], capturers, out var values)) return false;
        context.VariableFrame = WiredVariableRuntimeFrames.Create(context, context.VariableFrame);
        return Module.CaptureContextValues(values, context.VariableFrame);
    }
}
