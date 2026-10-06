namespace Plus.HabboHotel.Users.Messenger;

public class MessengerRequest
{
    public int ToId { get; set; }

    public int FromId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Figure { get; set; } = string.Empty;
}
