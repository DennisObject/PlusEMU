using Plus.HabboHotel.Items.Wired.Boxes;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Addons;

/// <summary>Projects the legacy random addon into the active editor without changing its stored fields.</summary>
public static class WiredLegacyAddonConfigurationAdapter
{
    public static bool TryConvert(IWiredItem original, WiredBoxDescriptor descriptor, out WiredConfiguration configuration)
    {
        configuration = new();

        if (original is not AddonRandomEffectBox || descriptor.Category != WiredBoxCategory.Addon
            || descriptor.CanonicalName != "wf_xtra_random")
        {
            return false;
        }

        // Legacy HandleSave stores no settings. The stack engine always picks one action, with no history.
        // SetItems/StringData/BoolData/ItemsData never participate in that behavior; the editor selects no furni.
        configuration = new()
        {
            IntParams = [1, 0]
        };

        return true;
    }
}
