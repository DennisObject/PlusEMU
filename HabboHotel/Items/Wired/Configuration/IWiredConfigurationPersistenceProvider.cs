namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Replace generic storage with an atomic domain write; throw on failure before active publication.</summary>
public interface IWiredConfigurationPersistenceProvider
{
    void PersistConfiguration(WiredConfiguration validated);
}
