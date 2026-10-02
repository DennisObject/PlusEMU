namespace Plus.HabboHotel.Camera;

public sealed class CameraConfiguration
{
    public string RendererUrl { get; set; } = "http://127.0.0.1:3921/render";

    public string Bearer { get; set; } = "";

    public string OutputDirectory { get; set; } = "camera";

    public int TimeoutSeconds { get; set; } = 15;

    public int MaxConcurrency { get; set; } = 2;

    public CameraEffectOption[] Effects { get; set; } = [];
}

public sealed class CameraEffectOption
{
    public string Name { get; set; } = "";

    public int MinLevel { get; set; }
}
