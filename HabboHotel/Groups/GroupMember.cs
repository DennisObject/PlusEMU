namespace Plus.HabboHotel.Groups;

public class GroupMember
{
    public GroupMember(int id, string username, string look)
    {
        Id = id;
        Username = username;
        Look = look;
    }

    public int Id
    {
        get; set;
    }
    public string Username
    {
        get; set;
    }
    public string Look
    {
        get; set;
    }
}
