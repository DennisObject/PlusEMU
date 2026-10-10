namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Serialize edits to one box, validate before persistence, and publish only a durable configuration.</summary>
public static class WiredConfigurationSave
{
    public static bool TrySave(IWiredConfiguredItem box, WiredConfiguration proposed, IWiredConfigurationStore store,
        out string error, Func<uint, bool>? existsInRoom = null,
        Func<IWiredConfiguredItem, WiredConfiguration, Action, bool>? publish = null,
        Func<IWiredConfiguredItem, WiredConfiguration, WiredConfiguration>? prepare = null,
        Func<uint, bool>? isTemporaryInRoom = null)
    {
        // A room publisher owns the engine lock; never acquire it while holding the per-box edit lock.
        if (publish != null) {
            return TrySaveCore(box, proposed, store, out error, existsInRoom, publish, prepare, isTemporaryInRoom);
        }

        lock (box) {
            return TrySaveCore(box, proposed, store, out error, existsInRoom, null, prepare, isTemporaryInRoom);
        }
    }

    private static bool TrySaveCore(IWiredConfiguredItem box, WiredConfiguration proposed, IWiredConfigurationStore store,
        out string error, Func<uint, bool>? existsInRoom,
        Func<IWiredConfiguredItem, WiredConfiguration, Action, bool>? publish,
        Func<IWiredConfiguredItem, WiredConfiguration, WiredConfiguration>? prepare,
        Func<uint, bool>? isTemporaryInRoom)
    {
        error = "Invalid Wired configuration.";

        if (box.Descriptor.Support != WiredBoxSupport.Implemented || !WiredLegacyProtocol.IsWithinLimits(proposed)
            || WiredNativeEditorProjection.Supports(box.Descriptor.CanonicalName)
                && !WiredNativeEditorProjection.IsBound(box.Item.Id, box.Descriptor, proposed)) {
            return false;
        }

        if (box.Descriptor.CanonicalName == "wf_act_place_furni"
            && !Plus.HabboHotel.Items.Wired.Modern.Actions.WiredTemporaryFurnitureActions.TryDecodeEditor(proposed, out proposed)) {
            return false;
        }

        if (existsInRoom != null && (!proposed.SelectedItems.All(existsInRoom)
            || !proposed.SecondarySelectedItems.All(existsInRoom))) {
            return false;
        }

        var capturesTemplates = !box.Item.IsTemporary && box.Descriptor.CanonicalName == "wf_act_place_furni"
            && proposed.TemporaryPlacement != null && prepare != null;

        if (!capturesTemplates && HasTemporaryPicks(proposed, isTemporaryInRoom)) {
            return false;
        }

        // Save-only read preparation captures snapshots without changing the live box. Hydration never calls this.
        var prepared = prepare != null ? prepare(box, proposed) : proposed;

        if (!WiredLegacyProtocol.IsWithinLimits(prepared) || HasTemporaryPicks(prepared, isTemporaryInRoom)
            || existsInRoom != null && (!prepared.SelectedItems.All(existsInRoom)
                || !prepared.SecondarySelectedItems.All(existsInRoom))) {
            return false;
        }

        if (!box.TryValidateConfiguration(prepared, out var validated, out error)) {
            return false;
        }

        if (!WiredLegacyProtocol.IsWithinLimits(validated) || HasTemporaryPicks(validated, isTemporaryInRoom)
            || existsInRoom != null && (!validated.SelectedItems.All(existsInRoom)
                || !validated.SecondarySelectedItems.All(existsInRoom))) {
            error = "Invalid normalized Wired configuration.";

            return false;
        }

        if (publish != null) {
            if (!publish(box, validated, () => Persist(box, validated, store))) {
                error = "This Wired box is no longer attached to the room.";

                return false;
            }
        }
        else {
            Persist(box, validated, store);
            box.ApplyConfiguration(validated);
        }

        error = string.Empty;

        return true;
    }

    private static void Persist(IWiredConfiguredItem box, WiredConfiguration validated, IWiredConfigurationStore store)
    {
        if (box is IWiredConfigurationPersistenceProvider provider) {
            provider.PersistConfiguration(validated);
        }
        else {
            store.Save(box.Item.Id, box.Descriptor, validated);
        }
    }

    private static bool HasTemporaryPicks(WiredConfiguration configuration, Func<uint, bool>? isTemporaryInRoom) =>
        isTemporaryInRoom != null && (configuration.SelectedItems.Any(isTemporaryInRoom)
            || configuration.SecondarySelectedItems.Any(isTemporaryInRoom));
}
