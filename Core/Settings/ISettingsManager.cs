namespace Plus.Core.Settings;

public interface ISettingsManager
{
    string TryGetValue(string value);
    // Callers that need a compatibility default must distinguish a missing setting from "0".
    string TryGetValue(string value, string defaultValue) => TryGetValue(value);
    Task Reload();
}