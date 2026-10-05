using Plus.HabboHotel.Permissions;
using System.Collections.Concurrent;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Boxes.Effects;

internal class GiveUserBadgeBox : IWiredItem
{
    private readonly IAccessControl _access;

    public GiveUserBadgeBox(Room instance, Item item, IAccessControl access)
    {
        Instance = instance;
        Item = item;
        _access = access;
        SetItems = new();
    }

    public Room Instance { get; set; }

    public Item Item { get; set; }

    public WiredBoxType Type => WiredBoxType.EffectGiveUserBadge;

    public ConcurrentDictionary<uint, Item> SetItems { get; set; }

    public string StringData { get; set; } = string.Empty;

    public bool BoolData { get; set; }

    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        var unknown = packet.ReadInt();
        var badge = packet.ReadString();
        StringData = badge;
    }

    public bool Execute(params object[] @params)
    {
        if (@params == null || @params.Length == 0)
            return false;
        if (!_access.Can(Item.UserId, PermissionKeys.RoomItemWiredRewards))
            return false;
        var player = (Habbo)@params[0];
        if (player == null || player.Client == null)
            return false;
        var user = player.CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(player.Username);
        if (user == null)
            return false;
        if (string.IsNullOrEmpty(StringData))
            return false;
        if (player.Inventory.Badges.HasBadge(StringData))
            player.Client.Send(new WhisperComposer(user.VirtualId, "Oops, it appears you have already recieved this badge!", 0, user.LastBubble));
        else
        {
            //player.Inventory.Badges.GiveBadge(StringData, true, player.GetClient());
            // TODO @80O: Inject BadgeManager
            player.Client.SendNotification("You have recieved a badge!");
        }
        return true;
    }

    internal GiveUserBadgeBox CreateCandidate() => new(Instance, Item, _access);
}