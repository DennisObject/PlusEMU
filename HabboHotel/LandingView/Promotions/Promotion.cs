namespace Plus.HabboHotel.LandingView.Promotions;

// Every text column of server_landing may be NULL.
public class Promotion
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? ButtonText { get; set; }
    public int ButtonType { get; set; }
    public string? ButtonLink { get; set; }
    public string? ImageLink { get; set; }
}
