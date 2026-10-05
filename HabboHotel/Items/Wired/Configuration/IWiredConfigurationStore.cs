namespace Plus.HabboHotel.Items.Wired.Configuration;

public interface IWiredConfigurationStore
{
    WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor);
    void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration);
    /// <summary>
    /// Forgets what was saved for these boxes - their configuration, legacy settings, reward claims and the values of the
    /// variables they define - all at once or not at all. Values these boxes hold for other variables are kept.
    /// </summary>
    void Reset(IReadOnlyCollection<uint> itemIds);
}
