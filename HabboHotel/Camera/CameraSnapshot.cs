using System.Globalization;
using System.Text.RegularExpressions;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Camera;

public sealed record CameraDoor(int X, int Y, int Z, int Direction);

public sealed record CameraSceneItem(
    uint Id,
    int SpriteId,
    string Type,
    int X,
    int Y,
    double Z,
    int Direction,
    int State,
    string WallPosition,
    string ExtraData);

public sealed record CameraSceneUser(
    int Id,
    int RoomIndex,
    string Figure,
    string Gender,
    int Type,
    int X,
    int Y,
    double Z,
    int Direction,
    int HeadDirection,
    string Posture,
    string PostureParameter,
    string Gesture,
    int Effect,
    int HandItem,
    int Dance);

public sealed record CameraScene(
    uint RoomId,
    string Heightmap,
    CameraDoor Door,
    int WallHeight,
    int WallThickness,
    int FloorThickness,
    bool HideWalls,
    string Floor,
    string Wallpaper,
    string Landscape,
    int BackgroundColor,
    IReadOnlyList<CameraSceneItem> Items,
    IReadOnlyList<CameraSceneUser> Users);

internal readonly record struct CameraRoomShell(
    uint RoomId,
    string? Heightmap,
    int DoorX,
    int DoorY,
    int DoorZ,
    int DoorDirection,
    int WallHeight,
    int WallThickness,
    int FloorThickness,
    bool HideWalls,
    string? Floor,
    string? Wallpaper,
    string? Landscape);

public static class CameraSnapshotBuilder
{
    public const int MaxItems = 5000;
    public const int MaxUsers = 1000;
    public const int MaxHeightmapChars = 65536;
    public const int AvatarUser = 1;
    public const int AvatarPet = 2;
    public const int AvatarRentableBot = 4;

    private static readonly HashSet<string> RendererGestures = new(StringComparer.Ordinal)
    {
        "sml", "agr", "srp", "sad", "joy", "crz", "tng", "eyb", "mis", "puz"
    };

    private static readonly Regex WallPosition = new(
        @"^:w=(-?\d+),(-?\d+)\s+l=(-?\d+),(-?\d+)\s+([lr])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExternalAddress = new(
        @"(?:https?:|data:|blob:|javascript:|file:|//)|\u0000",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static CameraScene Build(Room room) => Capture(room);

    public static CameraScene Capture(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);
        var map = room.GetGameMap();
        var model = map.Model;
        var shell = new CameraRoomShell(
            room.RoomId,
            model.GetRelativeHeightmap(),
            model.DoorX,
            model.DoorY,
            model.DoorZ,
            model.DoorOrientation,
            map.StaticModel.WallHeight,
            room.WallThickness,
            room.FloorThickness,
            room.Hidewall,
            room.Floor,
            room.Wallpaper,
            room.Landscape);
        var items = room.GetRoomItemHandler().GetFloor.ToArray()
            .Concat(room.GetRoomItemHandler().GetWall.ToArray())
            .ToArray();
        var users = room.GetRoomUserManager().GetRoomUsers().ToArray();
        return Compose(shell, items, users);
    }

    internal static CameraScene Compose(CameraRoomShell shell, IReadOnlyList<Item> items, IReadOnlyList<RoomUser> users)
    {
        if (items.Count > MaxItems || users.Count > MaxUsers) throw new InvalidOperationException("Room exceeds camera object limits");
        var itemCopy = items.ToArray();
        var userCopy = users.ToArray();
        var sceneItems = new List<CameraSceneItem>(Math.Min(itemCopy.Length, MaxItems));
        foreach (var item in itemCopy)
        {
            if (sceneItems.Count >= MaxItems)
                break;
            if (TryReadItem(item, out var sceneItem))
                sceneItems.Add(sceneItem);
        }

        var sceneUsers = new List<CameraSceneUser>(Math.Min(userCopy.Length, MaxUsers));
        foreach (var user in userCopy)
        {
            if (sceneUsers.Count >= MaxUsers)
                break;
            if (TryReadUser(user, out var sceneUser))
                sceneUsers.Add(sceneUser);
        }

        return new CameraScene(
            shell.RoomId,
            Paint(NormalizeHeightmap(shell.Heightmap)),
            new CameraDoor(shell.DoorX, shell.DoorY, shell.DoorZ, shell.DoorDirection),
            shell.WallHeight,
            shell.WallThickness,
            shell.FloorThickness,
            shell.HideWalls,
            Paint(shell.Floor),
            Paint(shell.Wallpaper),
            Paint(shell.Landscape),
            0,
            sceneItems,
            sceneUsers);
    }

    internal static bool TryReadItem(Item? item, out CameraSceneItem sceneItem)
    {
        sceneItem = null!;
        if (item?.Definition == null || item.Id < 1 || !double.IsFinite(item.GetZ) || item.Rotation is < 0 or > 7)
            return false;
        if (item.Definition.InteractionType is InteractionType.CameraPicture or InteractionType.Background)
            return false;
        string extra;
        try
        {
            extra = item.ExtraData?.Serialize() ?? "";
        }
        catch (Exception)
        {
            return false;
        }

        if (ExternalAddress.IsMatch(extra) || ExternalAddress.IsMatch(item.WallCoordinates ?? ""))
            return false;
        var type = item.Definition.Type == ItemType.Wall ? "i" : "s";
        var wallPosition = item.WallCoordinates ?? "";
        if (type == "i" && !WallPosition.IsMatch(wallPosition))
            return false;
        var state = int.TryParse(extra, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        sceneItem = new CameraSceneItem(
            item.Id,
            item.Definition.SpriteId,
            type,
            item.GetX,
            item.GetY,
            item.GetZ,
            item.Rotation,
            state,
            wallPosition,
            extra);
        return true;
    }

    internal static bool TryReadUser(RoomUser? user, out CameraSceneUser sceneUser)
    {
        sceneUser = null!;
        if (user == null || user.VirtualId < 0 || !double.IsFinite(user.Z) || user.RotBody is < 0 or > 7 || user.RotHead is < 0 or > 7)
            return false;
        var statuses = user.Statusses == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(user.Statusses, StringComparer.Ordinal);
        var (posture, parameter, gesture) = Motions(statuses, user.IsWalking);
        if (ExternalAddress.IsMatch(parameter) || ExternalAddress.IsMatch(gesture))
            return false;

        try
        {
            if (user.IsPet)
            {
                var petLook = user.PetData?.Look ?? "";
                if (user.PetData == null || ExternalAddress.IsMatch(petLook))
                    return false;
                sceneUser = User(
                    user.BotAi?.BaseId ?? user.PetData.PetId,
                    user, petLook, "", AvatarPet, posture, parameter, gesture, (int)user.CurrentItemEffect);
                return true;
            }

            if (user.IsBot)
            {
                if (user.BotData == null)
                    return false;
                var figure = user.BotData.Look ?? "";
                var gender = (user.BotData.Gender ?? "").ToLowerInvariant();
                if (ExternalAddress.IsMatch(figure) || ExternalAddress.IsMatch(gender))
                    return false;
                sceneUser = User(
                    user.BotAi?.BaseId ?? user.BotData.BotId,
                    user, figure, gender, AvatarRentableBot, posture, parameter, gesture, (int)user.CurrentItemEffect);
                return true;
            }

            var habbo = user.GetClient()?.GetHabbo();
            if (habbo == null)
                return false;
            var look = habbo.Look ?? "";
            var userGender = (habbo.Gender ?? "").ToLowerInvariant();
            if (ExternalAddress.IsMatch(look) || ExternalAddress.IsMatch(userGender))
                return false;
            var effect = habbo.Effects?.CurrentEffect ?? 0;
            sceneUser = User(habbo.Id, user, look, userGender, AvatarUser, posture, parameter, gesture, effect);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static (string Posture, string Parameter, string Gesture) Motions(IReadOnlyDictionary<string, string> statuses, bool walking)
    {
        string posture;
        string parameter;
        if (statuses.TryGetValue("lay", out var lay))
        {
            posture = "lay";
            parameter = lay ?? "";
        }
        else if (statuses.TryGetValue("sit", out var sit))
        {
            posture = "sit";
            parameter = sit ?? "";
        }
        else if (walking || statuses.ContainsKey("mv"))
        {
            posture = "mv";
            parameter = statuses.TryGetValue("mv", out var move) ? move ?? "" : "";
        }
        else
        {
            posture = "std";
            parameter = "";
        }

        var gesture = statuses.TryGetValue("gst", out var acted) && !string.IsNullOrEmpty(acted)
            ? acted
            : statuses.TryGetValue("sign", out var sign) && !string.IsNullOrEmpty(sign) ? sign : "";
        return (posture, parameter, gesture);
    }

    private static CameraSceneUser User(
        int id, RoomUser user, string figure, string gender, int type,
        string posture, string parameter, string gesture, int effect) =>
        new(id, user.VirtualId, figure, gender, type, user.X, user.Y, user.Z, user.RotBody, user.RotHead,
            posture, parameter, RendererGestures.Contains(gesture) ? gesture : "", effect, user.CarryItemId, user.DanceId);

    internal static string NormalizeHeightmap(string? heightmap)
    {
        if (string.IsNullOrEmpty(heightmap))
            return "";
        var normalized = heightmap.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\n', '\r');
        if (normalized.Length > MaxHeightmapChars) throw new InvalidOperationException("Room exceeds camera map limit");
        return normalized;
    }

    private static string Paint(string? value)
    {
        if (string.IsNullOrEmpty(value) || ExternalAddress.IsMatch(value))
            return "";
        return value;
    }
}
