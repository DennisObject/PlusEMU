using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Boxes.Effects;

internal class TeleportUserBox : IWiredItem, IWiredCycle, IWiredFiringPreparation
{
    private int _delay;

    public TeleportUserBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
        TickCount = Delay;
    }

    public int Delay
    {
        get => _delay;
        set
        {
            _delay = value;
            TickCount = value + 1;
        }
    }

    public int TickCount { get; set; }

    // Delayed firings are independently retained by the room engine.
    public bool OnCycle() => false;

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.EffectTeleportToFurni;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        var unknown = packet.ReadInt();
        var unknown2 = packet.ReadString();
        if (SetItems.Count > 0)
            SetItems.Clear();
        var furniCount = packet.ReadInt();
        for (var i = 0; i < furniCount; i++)
        {
            var selectedItem = Instance.GetRoomItemHandler().GetItem(packet.ReadUInt());
            if (selectedItem != null)
                SetItems.TryAdd(selectedItem.Id, selectedItem);
        }
        Delay = packet.ReadInt();
    }

    public bool Prepare(params object[] @params)
    {
        if (@params == null || @params.Length == 0)
            return false;
        var player = (Habbo)@params[0];
        if (player == null || player.CurrentRoom != Instance || SetItems.Count == 0)
            return false;
        if (player.Effects != null)
            player.Effects.ApplyEffect(4);
        return true;
    }

    public bool Execute(params object[] @params)
    {
        if (@params.Length == 0 || @params[0] is not Habbo player || player.CurrentRoom != Instance || SetItems.Count == 0)
            return false;
        TeleportUser(player);
        return true;
    }

    private void TeleportUser(Habbo player)
    {
        if (player == null)
            return;
        var room = player.CurrentRoom;
        if (room == null)
            return;
        var user = player.CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(player.Username);
        if (user == null)
            return;
        if (player.IsTeleporting || player.IsHopping || player.TeleporterId != 0)
            return;
        var items = SetItems.Values.ToList();
        items = items.OrderBy(x => Random.Shared.Next()).ToList();
        if (items.Count == 0)
            return;
        var item = items.First();
        if (item == null)
            return;
        if (!Instance.GetRoomItemHandler().GetFloor.Contains(item))
        {
            SetItems.TryRemove(item.Id, out item);
            if (items.Contains(item))
                items.Remove(item);
            if (SetItems.Count == 0 || items.Count == 0)
                return;
            item = items.First();
            if (item == null)
                return;
        }
        if (room.GetGameMap() == null)
            return;
        room.GetGameMap().TeleportToItem(user, item);
        room.GetRoomUserManager().UpdateUserStatusses();
        if (player.Effects != null)
            player.Effects.ApplyEffect(0);
    }
}