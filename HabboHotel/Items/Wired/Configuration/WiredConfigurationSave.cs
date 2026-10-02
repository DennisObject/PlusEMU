namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Serialize edits to one box, validate before persistence, and publish only a durable configuration.</summary>
public static class WiredConfigurationSave
{
    public static bool TrySave(IWiredConfiguredItem box, WiredConfiguration proposed, IWiredConfigurationStore store,
        out string error, Func<uint, bool>? existsInRoom = null)
    {
        lock (box)
        {
            error = "Invalid Wired configuration.";
            if (box.Descriptor.Support != WiredBoxSupport.Implemented || !WiredLegacyProtocol.IsWithinLimits(proposed))
                return false;
            if (existsInRoom != null && (!proposed.SelectedItems.All(existsInRoom)
                || !proposed.SecondarySelectedItems.All(existsInRoom)))
                return false;
            if (!box.TryValidateConfiguration(proposed, out var validated, out error))
                return false;
            if (!WiredLegacyProtocol.IsWithinLimits(validated)
                || existsInRoom != null && (!validated.SelectedItems.All(existsInRoom)
                    || !validated.SecondarySelectedItems.All(existsInRoom)))
            {
                error = "Invalid normalized Wired configuration.";
                return false;
            }
            store.Save(box.Item.Id, box.Descriptor, validated);
            box.ApplyConfiguration(validated);
            error = string.Empty;
            return true;
        }
    }
}
