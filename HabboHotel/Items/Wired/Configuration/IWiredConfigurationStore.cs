namespace Plus.HabboHotel.Items.Wired.Configuration;

public interface IWiredConfigurationStore
{
    WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor);
    void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration);
}
