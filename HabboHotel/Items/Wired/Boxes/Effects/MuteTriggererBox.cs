using Plus.HabboHotel.Permissions;
using System.Collections.Concurrent;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.Utilities;

namespace Plus.HabboHotel.Items.Wired.Boxes.Effects;

internal class MuteTriggererBox : IWiredItem
{
    public MuteTriggererBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
        if (SetItems.Count > 0)
            SetItems.Clear();
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.EffectMuteTriggerer;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; }
    public bool BoolData { get; set; }
    public string ItemsData { get; set; }

    public void HandleSave(IIncomingPacket packet)
    {
        if (SetItems.Count > 0)
            SetItems.Clear();
        var unknown = packet.ReadInt();
        var time = packet.ReadInt();
        var message = packet.ReadString();
        StringData = $"{time};{message}";
    }

    public bool Execute(params object[] @params)
    {
        if (@params.Length != 1)
            return false;
        var player = (Habbo)@params[0];
        if (player == null)
            return false;
        var user = Instance.GetRoomUserManager().GetRoomUserByHabbo(player.Id);
        if (user == null)
            return false;
        if (player.Access.Can(PermissionKeys.ModerationTool) || Instance.OwnerId == player.Id)
        {
            player.Client.Send(new WhisperComposer(user.VirtualId, "Wired Mute Exception: Unmutable Player", 0, 0));
            return false;
        }
        var time = StringData != null ? int.Parse(StringData.Split(';')[0]) : 0;
        var message = StringData != null ? StringData.Split(';')[1] : "No message!";
        if (time > 0)
        {
            player.Client.Send(new WhisperComposer(user.VirtualId, $"Wired Mute: Muted for {time}! Message: {message}", 0, 0));
            if (!Instance.MutedUsers.ContainsKey(player.Id))
                Instance.MutedUsers.Add(player.Id, UnixTimestamp.GetNow() + time * 60);
            else
            {
                Instance.MutedUsers.Remove(player.Id);
                Instance.MutedUsers.Add(player.Id, UnixTimestamp.GetNow() + time * 60);
            }
        }
        return true;
    }
}