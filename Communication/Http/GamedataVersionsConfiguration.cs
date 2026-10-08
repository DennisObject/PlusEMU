namespace Plus.Communication.Http;

// "GamedataVersions" section of config.json: gamedata files the client loads from a CDN (e.g. ExternalTexts.json
// on assets.<hotel>) whose current version the entry page passes on, so the client can request them by version.
public sealed class GamedataVersionsConfiguration
{
    // Base URL the files are served from, e.g. "https://assets.example.com/gamedata/". Empty turns the feature off.
    public string BaseUrl { get; set; } = "";

    // File names under BaseUrl, matching the last path segment of the client's gamedata URLs.
    public string[] Files { get; set; } = [];

    // How long a looked-up version is served before it is looked up again (in the background).
    public int RefreshSeconds { get; set; } = 30;

    // Per lookup; a file whose lookup fails or times out is left out, and the client falls back to its cache buster.
    public int TimeoutMilliseconds { get; set; } = 1500;
}
