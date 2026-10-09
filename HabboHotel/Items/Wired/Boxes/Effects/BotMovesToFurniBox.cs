using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Boxes.Effects;

internal class BotMovesToFurniBox : IWiredItem
{
    public BotMovesToFurniBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.EffectBotMovesToFurniBox;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        var unknown = packet.ReadInt();
        var botName = packet.ReadString();

        if (SetItems.Count > 0) {
            SetItems.Clear();
        }

        var furniCount = packet.ReadInt();

        for (var i = 0; i < furniCount; i++) {
            var selectedItem = Instance.GetRoomItemHandler().GetItem(packet.ReadUInt());

            if (selectedItem != null) {
                SetItems.TryAdd(selectedItem.Id, selectedItem);
            }
        }

        StringData = botName;
    }

    public bool Execute(params object[] @params)
    {
        if (@params == null || @params.Length == 0 || string.IsNullOrEmpty(StringData)) {
            return false;
        }

        var user = Instance.GetRoomUserManager().GetBotByName(StringData);

        if (user?.BotData is not { } botData) {
            return false;
        }

        var items = SetItems.Values.ToList();

        if (items.Count == 0) {
            return false;
        }

        items = items.OrderBy(x => Random.Shared.Next()).ToList();
        var item = items.First();

        if (item == null) {
            return false;
        }

        if (!Instance.GetRoomItemHandler().GetFloor.Contains(item)) {
            SetItems.TryRemove(item.Id, out _);

            if (items.Contains(item)) {
                items.Remove(item);
            }

            if (SetItems.Count == 0 || items.Count == 0) {
                return false;
            }

            item = items.First();

            if (item == null) {
                return false;
            }
        }

        if (Instance.GetGameMap() == null) {
            return false;
        }

        if (user.IsWalking) {
            user.ClearMovement(true);
        }

        botData.ForcedMovement = true;
        botData.TargetCoordinate = new(item.GetX, item.GetY);
        user.MoveTo(item.GetX, item.GetY);

        return true;
    }
}
