namespace Plus.HabboHotel.Items.Editor;

// "FurniEditor" section of config.json.
public sealed class FurniEditorConfiguration
{
    public static readonly string[] OfficialHabboHosts =
        ["habbo.com", "habbo.com.br", "habbo.com.tr", "habbo.de", "habbo.es", "habbo.fi", "habbo.fr", "habbo.it", "habbo.nl"];

    // Absolute path of the FurnitureData.json the hotel serves. Empty turns furnidata edits off;
    // the emulator needs write access to the file and its directory (for the temp file and the .bak copy).
    public string FurnidataPath { get; set; } = "";

    public long FurnidataMaxBytes { get; set; } = 64 * 1024 * 1024;

    // Official furnidata JSON used by "Import from Habbo". Empty turns the import off.
    public string ImportUrl { get; set; } = "";

    // Hosts (and their subdomains) the import may contact, redirects included. Empty means the official Habbo hotels.
    public string[] ImportAllowedHosts { get; set; } = [];

    public int ImportTimeoutSeconds { get; set; } = 10;

    public long ImportMaxBytes { get; set; } = 48 * 1024 * 1024;

    public IReadOnlyList<string> AllowedImportHosts => ImportAllowedHosts.Length > 0 ? ImportAllowedHosts : OfficialHabboHosts;
}
