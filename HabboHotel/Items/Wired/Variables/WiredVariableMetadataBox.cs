using System.Globalization;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Variables.Fx;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Passive metadata consumed by the room's variable presentation, never a successful stack action.</summary>
public sealed class WiredVariableMetadataBox : WiredConfiguredBehaviorBox
{
    public WiredVariableMetadataBox(Room room, Item item, WiredBoxDescriptor descriptor)
        : base(room, item, descriptor, configuration => Validate(descriptor.CanonicalName, configuration))
    {
        ConfigurationChanged();
    }
    public WiredVariableTimeUtilities? TimeUtilities { get; private set; }
    public WiredVariableLevelSystem? LevelSystem { get; private set; }
    public IReadOnlyDictionary<int, string> TextConnector { get; private set; } = new Dictionary<int, string>();
    public static bool Supports(string name) => IsFx(name) || name is "wf_xtra_var_lvlup_system" or "wf_xtra_var_text_connector" or "wf_xtra_var_time_util";
    public string[] DerivedKeys => ["current_level", "current_xp", "level_progress", "level_progress_percent", "total_xp_required", "xp_remaining", "is_at_max", "max_level"];
    public bool HasDerived(int sub) => sub >= 0 && sub < DerivedKeys.Length && LevelSystem is { } level && (level.SubvariableMask & (1 << sub)) != 0;
    public long ReadDerived(long value, int sub)
    {
        if (!HasDerived(sub)) {
            throw new ArgumentOutOfRangeException(nameof(sub));
        }

        return LevelSystem!.Read(value, sub);
    }
    public static bool IsFx(string name) => name is "wf_xtra_var_fx_health" or "wf_xtra_var_fx_progress" or "wf_xtra_var_fx_level"
        or "wf_xtra_var_fx_status" or "wf_xtra_var_fx_boss" or "wf_xtra_var_fx_number";
    protected override void ConfigurationChanged()
    {
        if (Descriptor.CanonicalName == "wf_xtra_var_time_util") {
            TimeUtilities = new(Configuration.IntParams[0], Configuration.IntParams[1]);
        }

        if (Descriptor.CanonicalName == "wf_xtra_var_lvlup_system") {
            WiredVariableLevelSystem.TryParse(Configuration.Text, out var level);
            LevelSystem = level;
        }

        if (Descriptor.CanonicalName == "wf_xtra_var_text_connector") {
            TextConnector = ParseConnector(Configuration.Text);
        }
    }
    internal static WiredConfiguration Validate(string name, WiredConfiguration configuration)
    {
        if (configuration.Version != 1 || configuration.Text.Length > 8192) {
            throw new ArgumentException("Invalid variable metadata.");
        }

        if (IsFx(name)) {
            // The unconfigured box before its native defaults are applied has nothing to decode yet.
            if (configuration.IntParams.IsEmpty && configuration.Text.Length == 0 && configuration.VariableIds.IsEmpty) {
                return configuration;
            }

            var target = WiredVariableFxSettings.UsesUserSource(configuration) ? WiredVariableTarget.User : WiredVariableTarget.Furni;

            if (!WiredVariableFxSettings.TryDecode(name, 1, configuration, new(target, ""), null, out _, out var error)) {
                throw new ArgumentException(error);
            }
        }
        else if (name == "wf_xtra_var_lvlup_system") {
            if (configuration.Text.Length == 0) {
                configuration = configuration with { Text = "{\"mode\":1,\"stepSize\":100,\"maxLevel\":10,\"firstLevelXp\":100,\"increaseFactor\":100,\"interpolationText\":\"\",\"subvariables\":[0,1]}" };
            }

            if (!WiredVariableLevelSystem.TryParse(configuration.Text, out _)) {
                throw new ArgumentException("Invalid level system.");
            }
        }
        else if (name == "wf_xtra_var_time_util") {
            if (configuration.IntParams.Length == 0) {
                configuration = configuration with { IntParams = [0, 0] };
            }

            if (configuration.IntParams.Length != 2 || configuration.Text.Length != 0) {
                throw new ArgumentException("Invalid time utility configuration.");
            }

            // The editor only offers the valid subvariables and three clocks; anything else is refused, not trimmed.
            if ((configuration.IntParams[0] & WiredVariableTimeUtilities.ValidMask) != configuration.IntParams[0] || configuration.IntParams[1] is < 0 or > 2) {
                throw new ArgumentException("Invalid time utility configuration.");
            }
        }
        else if (name == "wf_xtra_var_text_connector") {
            ParseConnector(configuration.Text);
        }
        else {
            throw new ArgumentException("Unsupported variable metadata.");
        }

        return configuration;
    }
    internal static IReadOnlyDictionary<int, string> ParseConnector(string text)
    {
        if (text.Length > 1000) {
            throw new ArgumentException("Text connector is too long.");
        }

        var result = new Dictionary<int, string>();
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length > 30) {
            throw new ArgumentException("Text connector has too many entries.");
        }

        foreach (var line in lines) {
            var separator = line.IndexOf('=');

            if (separator <= 0 || !int.TryParse(line[..separator].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var key)
                || string.IsNullOrWhiteSpace(line[(separator + 1)..])) {
                throw new ArgumentException("Invalid text connector entry.");
            }

            result[key] = line[(separator + 1)..].Trim();
        }

        return result;
    }
}
