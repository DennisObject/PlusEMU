namespace Plus.HabboHotel.Items.Wired.Configuration;

public interface IWiredConfiguredItem : IWiredItem
{
    WiredBoxDescriptor Descriptor { get; }
    WiredConfiguration Configuration { get; }

    /// <summary>Pure validation: reject without changing the live box; decode only this box's editor schema.</summary>
    bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error);

    /// <summary>Publish an already validated configuration. Must not fail or perform persistence.</summary>
    void ApplyConfiguration(WiredConfiguration validated);
}
