using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired;

internal static class WiredBoxLoading
{
    // A shared canonical name never discards the original wired_items representation.
    public static IWiredItem? Select(IWiredItem? legacy, IWiredConfiguredItem? configured, WiredConfiguration? saved)
    {
        if (saved == null && legacy != null) return legacy;
        if (configured == null)
        {
            if (saved != null) throw new InvalidDataException("The saved Wired behavior is unavailable.");
            return legacy;
        }
        if (configured.Descriptor.Support != WiredBoxSupport.Implemented)
            throw new InvalidDataException("The Wired descriptor has no executable factory.");
        // Fresh boxes retain editor drafts and no persistence authority until a successful save.
        if (saved == null) return configured;
        if (!configured.TryValidateConfiguration(saved, out var validated, out var error))
            throw new InvalidDataException(error);
        configured.ApplyConfiguration(validated);
        return configured;
    }
}
