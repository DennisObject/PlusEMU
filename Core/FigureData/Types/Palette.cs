namespace Plus.Core.FigureData.Types;

public class Palette
{
    public Palette(int id)
    {
        Id = id;
        Colors = new();
    }

    public int Id { get; set; }
    public Dictionary<int, Color> Colors { get; set; }
}
