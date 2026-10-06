using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Moderation;

public enum ModerationTicketStatus { Open = 1, Assigned = 2, ClosedOrAssignedElsewhere = 3 }

public class ModerationTicket
{
    public List<string> ReportedChats;

    public ModerationTicket(int id, int type, int category, DateTimeOffset createdAt, int priority, Habbo sender, Habbo? reported, string issue, RoomData? room, List<string> reportedChats)
    {
        Id = id;
        Type = type;
        Category = category;
        CreatedAt = createdAt.ToUniversalTime();
        Priority = priority;
        Sender = sender;
        Reported = reported;
        Moderator = null;
        Issue = issue;
        Room = room;
        Answered = false;
        ReportedChats = reportedChats.ToList();
    }

    public int Id { get; set; }
    public int Type { get; set; }
    public int Category { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Priority { get; set; }
    public bool Answered { get; set; }
    public Habbo Sender { get; set; }
    public Habbo? Reported { get; set; }
    public Habbo? Moderator { get; set; }
    public string Issue { get; set; }
    public RoomData? Room { get; set; }

    public ModerationTicketStatus GetStatus(int id)
    {
        if (Moderator == null)
            return ModerationTicketStatus.Open;
        if (Moderator.Id == id && !Answered)
            return ModerationTicketStatus.Assigned;
        if (Answered)
            return ModerationTicketStatus.ClosedOrAssignedElsewhere;
        return ModerationTicketStatus.ClosedOrAssignedElsewhere;
    }
}