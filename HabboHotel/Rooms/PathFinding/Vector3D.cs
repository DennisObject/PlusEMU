namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class Vector3D
{
    public Vector3D()
    {
    }

    public Vector3D(int x, int y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public int X { get; set; }

    public int Y { get; set; }

    public double Z { get; set; }

    public Vector2D ToVector2D() => new(X, Y);
}
