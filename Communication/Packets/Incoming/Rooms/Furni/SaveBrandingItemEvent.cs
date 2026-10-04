using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class SaveBrandingItemEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return Task.CompletedTask;
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return Task.CompletedTask;
        if (!room.CheckRights(session, true) || !session.GetHabbo().Access.Can(PermissionKeys.RoomItemSaveBrandingItems))
            return Task.CompletedTask;
        var itemId = packet.ReadUInt();
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary)
            return Task.CompletedTask;
        if (item.Definition.InteractionType == InteractionType.Background)
        {
            var count = packet.ReadInt();
            if (count < 0 || count > 128 || count % 2 != 0) return Task.CompletedTask;
            var values = new List<string>(count);
            for (var i = 1; i <= count; i++) values.Add(packet.ReadString());
            if (FurniExtraData.RejectsClientImage(values))
                return Task.CompletedTask;
            var data = new Dictionary<string, string> { ["state"] = "0" };
            for (var index = 0; index < values.Count; index += 2) data[values[index]] = values[index + 1];
            item.ExtraData = new MapDataFormat(data);
        }
        else if (item.Definition.InteractionType == InteractionType.FxProvider)
        {
            /*int Unknown = Packet.PopInt();
            string Data = Packet.PopString();
            int EffectId = Packet.PopInt();

            Item.ExtraData = Convert.ToString(EffectId);*/
        }
        room.GetRoomItemHandler().SetFloorItem(session, item, item.GetX, item.GetY, item.Rotation, false, false, true);
        return Task.CompletedTask;
    }
}