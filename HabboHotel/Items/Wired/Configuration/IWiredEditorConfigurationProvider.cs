namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Read-only editor projection; never changes saved configuration or captures replacement snapshots.</summary>
public interface IWiredEditorConfigurationProvider
{
    WiredConfiguration GetEditorConfiguration();
}
