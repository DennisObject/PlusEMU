namespace Plus.HabboHotel.Items.Editor;

// "FurniEditor" section of config.json.
public sealed class FurniEditorConfiguration
{
    // Absolute path of the FurnitureData.json the hotel serves. Empty turns furnidata edits off;
    // the emulator needs write access to the file and its directory (for the temp file and the .bak copy).
    public string FurnidataPath { get; set; } = "";

    public long FurnidataMaxBytes { get; set; } = 64 * 1024 * 1024;

    // Official furnidata JSON used by "Import from Habbo". Empty turns the import off.
    public string ImportUrl { get; set; } = "";

    public int ImportTimeoutSeconds { get; set; } = 20;
}
