namespace Plus.Communication.Flash;

public class FlashRevision
{
    public string Revision { get; set; } = string.Empty;
    public Dictionary<int, int> HeaderMapping { get; set; } = new();
}
