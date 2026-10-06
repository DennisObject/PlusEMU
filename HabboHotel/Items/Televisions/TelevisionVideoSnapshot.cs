namespace Plus.HabboHotel.Items.Televisions;

public sealed record TelevisionVideoSnapshot(string YouTubeId, string Title, string Description)
{
    public static TelevisionVideoSnapshot Capture(TelevisionItem television) => new(television.YouTubeId, television.Title, television.Description);
}
