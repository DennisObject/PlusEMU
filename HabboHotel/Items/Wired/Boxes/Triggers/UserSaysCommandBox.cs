using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using System.Collections.Concurrent;

namespace Plus.HabboHotel.Items.Wired.Boxes.Triggers;

internal class UserSaysCommandBox : IWiredItem
{
    public UserSaysCommandBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        StringData = "";
        SetItems = new();
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.TriggerUserSaysCommand;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; }
    public bool BoolData { get; set; }
    public string ItemsData { get; set; }

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
        if (BoolData && Instance.OwnerId != player.Id || string.IsNullOrWhiteSpace(StringData))
            return false;
        if (!PlusEnvironment.Game.ChatManager.GetCommands().TryGetCommand(StringData.Replace(":", "").ToLower(), out var chatCommand))
            return false;
        if (player.ChatCommand == chatCommand)
        {
            return Instance.GetWired().RunStack(this, [player], () =>
            {
                player.WiredInteraction = true;
                player.Client.Send(new WhisperComposer(user.VirtualId, StringData, 0, 0));
            });
        }
        return false;
    }
}
