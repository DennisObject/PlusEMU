using System.Collections.Concurrent;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Boxes.Triggers;

internal class UserSaysBox : IWiredItem
{
    public UserSaysBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        StringData = "";
        SetItems = new();
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.TriggerUserSays;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        var unknown = packet.ReadInt();
        var ownerOnly = packet.ReadInt();
        var message = packet.ReadString();
        BoolData = ownerOnly == 1;
        StringData = message;
    }

    public bool Execute(params object[] @params)
    {
        var player = (Habbo)@params[0];
        if (player == null || player.CurrentRoom == null || !player.InRoom)
            return false;
        var user = player.CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(player.Username);
        if (user == null)
            return false;
        var message = Convert.ToString(@params[1]);
        if (BoolData && Instance.OwnerId != player.Id || player == null || string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(StringData))
            return false;
        if (message.Contains($" {StringData}") || message.Contains($"{StringData} ") || message == StringData)
        {
            return Instance.GetWired().RunStack(this, [player], () =>
            {
                player.WiredInteraction = true;
                player.Client.Send(new WhisperComposer(user.VirtualId, message, 0, 0));
            });
        }
        return false;
    }
}
