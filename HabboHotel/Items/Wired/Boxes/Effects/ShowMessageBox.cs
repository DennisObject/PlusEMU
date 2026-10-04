using System.Collections.Concurrent;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Boxes.Effects;

internal class ShowMessageBox : IWiredItem
{
    public ShowMessageBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
    }

    public Room Instance { get; set; }

    public Item Item { get; set; }

    public WiredBoxType Type => WiredBoxType.EffectShowMessage;

    public ConcurrentDictionary<uint, Item> SetItems { get; set; }

    public string StringData { get; set; } = string.Empty;

    public bool BoolData { get; set; }

    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        var unknown = packet.ReadInt();
        var message = packet.ReadString();
        StringData = message;
    }

    public bool Execute(params object[] @params)
    {
        if (@params == null || @params.Length == 0)
            return false;
        var player = (Habbo)@params[0];
        if (player == null || player.Client == null || string.IsNullOrWhiteSpace(StringData))
            return false;
        var user = player.CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(player.Username);
        if (user == null)
            return false;
        var message = StringData;
        if (StringData.Contains("%USERNAME%"))
            message = message.Replace("%USERNAME%", player.Username);
        if (StringData.Contains("%ROOMNAME%"))
            message = message.Replace("%ROOMNAME%", player.CurrentRoom.Name);
        if (StringData.Contains("%USERCOUNT%"))
            message = message.Replace("%USERCOUNT%", player.CurrentRoom.UserCount.ToString());
        if (StringData.Contains("%USERSONLINE%"))
            message = message.Replace("%USERSONLINE%", PlusEnvironment.Game.ClientManager.Count.ToString());
        player.Client.Send(new WhisperComposer(user.VirtualId, message, 0, 34));
        return true;
    }
}