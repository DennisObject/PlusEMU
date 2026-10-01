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
        if (!room.CheckRights(session, true) || !session.GetHabbo().Permissions.HasRight("room_item_save_branding_items"))
            return Task.CompletedTask;
        var itemId = packet.ReadUInt();
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null)
            return Task.CompletedTask;
        if (item.Definition.InteractionType == InteractionType.Background)
        {
            var count = packet.ReadInt();
            var values = new List<string>(Math.Max(count, 0));
            for (var i = 1; i <= count; i++) values.Add(packet.ReadString());
            var map = item.ExtraData as MapDataFormat ?? new MapDataFormat();
            map.Store(FurniExtraData.Branding(values));
            item.ExtraData = map;
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