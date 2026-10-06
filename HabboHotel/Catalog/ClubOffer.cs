namespace Plus.HabboHotel.Catalog;

public class ClubOffer
{
    public int Id
    {
        get; set;
    }
    public string Name { get; set; } = string.Empty;
    public int Days
    {
        get; set;
    }
    public int Credits
    {
        get; set;
    }
    public int Points
    {
        get; set;
    }
    public int PointsType
    {
        get; set;
    }
    public bool Giftable
    {
        get; set;
    }

    public int Months => Days / 31;
    public int ExtraDays => Days % 31;
}
