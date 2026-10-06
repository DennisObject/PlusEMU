using Plus.HabboHotel.Permissions;
using System.Collections.Concurrent;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Boxes.Effects;

internal class KickUserBox : IWiredItem, IWiredCycle, IWiredActionDelay, IWiredFiringPreparation
{

    public KickUserBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
        TickCount = Delay;

        if (SetItems.Count > 0) {
            SetItems.Clear();
        }
    }

    public int TickCount { get; set; }
    public int Delay { get; set; }
    public long DelayMilliseconds => 1500;

    // The legacy kick grace period is scheduled by the room, not a shared actor queue.
    public bool OnCycle() => false;

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.EffectKickUser;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        if (SetItems.Count > 0) {
            SetItems.Clear();
        }

        var unknown = packet.ReadInt();
        var message = packet.ReadString();
        StringData = message;
    }

    public bool Prepare(params object[] @params)
    {
        if (@params.Length != 1 || @params[0] is not Habbo player || player.CurrentRoom != Instance) {
            return false;
        }

        var user = Instance.GetRoomUserManager().GetRoomUserByHabbo(player.Id);

        if (user == null) {
            return false;
        }

        if (player.Access.Can(PermissionKeys.ModerationTool) || Instance.OwnerId == player.Id) {
            player.Client.Send(new WhisperComposer(user.VirtualId, "Wired Kick Exception: Unkickable Player", 0, 0));

            return false;
        }

        player.Client.Send(new WhisperComposer(user.VirtualId, StringData, 0, 0));

        return true;
    }

    public bool Execute(params object[] @params)
    {
        if (@params.Length != 1 || @params[0] is not Habbo player || player.CurrentRoom != Instance) {
            return false;
        }

        if (player.Access.Can(PermissionKeys.ModerationTool) || Instance.OwnerId == player.Id) {
            return false;
        }

        Instance.GetRoomUserManager().RemoveUserFromRoom(player.Client, true);

        return true;
    }
}
