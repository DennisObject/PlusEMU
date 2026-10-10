namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Serialize edits to one box, validate before persistence, and publish only a durable configuration.</summary>
public static class WiredConfigurationSave
{
    public static bool TrySave(IWiredConfiguredItem box, WiredConfiguration proposed, IWiredConfigurationStore store,
        out string error, Func<uint, bool>? existsInRoom = null,
        Func<IWiredConfiguredItem, WiredConfiguration, Action, bool>? publish = null,
        Func<uint, bool>? isTemporaryInRoom = null)
    {
        // A room publisher owns the engine lock; never acquire it while holding the per-box edit lock.
        if (publish != null) {
            return TrySaveCore(box, proposed, store, out error, existsInRoom, publish, isTemporaryInRoom);
        }

        lock (box) {
            return TrySaveCore(box, proposed, store, out error, existsInRoom, null, isTemporaryInRoom);
        }
    }

    private static bool TrySaveCore(IWiredConfiguredItem box, WiredConfiguration proposed, IWiredConfigurationStore store,
        out string error, Func<uint, bool>? existsInRoom,
        Func<IWiredConfiguredItem, WiredConfiguration, Action, bool>? publish,
        Func<uint, bool>? isTemporaryInRoom)
    {
        error = "Invalid Wired configuration.";

        if (box.Descriptor.Support != WiredBoxSupport.Implemented || !WiredLegacyProtocol.IsWithinLimits(proposed)
            || WiredNativeEditorProjection.Supports(box.Descriptor.CanonicalName)
                && !WiredNativeEditorProjection.IsBound(box.Item.Id, box.Descriptor, proposed)) {
            return false;
        }

        if (existsInRoom != null && (!proposed.SelectedItems.All(existsInRoom)
            || !proposed.SecondarySelectedItems.All(existsInRoom))) {
            return false;
        }

        if (HasTemporaryPicks(proposed, isTemporaryInRoom)) {
            return false;
        }

        if (!box.TryValidateConfiguration(proposed, out var validated, out error)) {
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
