using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Rooms.Settings;

public sealed class RoomSettingsDataComposer(RoomSettingsSnapshot data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.RoomSettingsDataComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(data.RoomId);
        packet.WriteString(data.Name);
        packet.WriteString(data.Description);
        packet.WriteInteger(data.Access);
        packet.WriteInteger(data.Category);
        packet.WriteInteger(data.UsersMax);
        packet.WriteInteger(data.CapacityLimit);
        packet.WriteInteger(data.Tags.Length);

        foreach (var tag in data.Tags.ToArray())
        {
            packet.WriteString(tag);
        }

        packet.WriteInteger(data.TradeSettings); //Trade
        packet.WriteInteger(data.AllowPets ? 1 : 0); // allows pets in room - pet system lacking, so always off
        packet.WriteInteger(data.AllowPetsEating ? 1 : 0); // allows pets to eat your food - pet system lacking, so always off
        packet.WriteInteger(data.RoomBlockingEnabled ? 1 : 0);
        packet.WriteInteger(data.Hidewall ? 1 : 0);
        packet.WriteInteger(data.WallThickness);
        packet.WriteInteger(data.FloorThickness);
        packet.WriteInteger(data.ChatMode); //Chat mode
        packet.WriteInteger(data.ChatSize); //Chat size
        packet.WriteInteger(data.ChatSpeed); //Chat speed
        packet.WriteInteger(data.ChatDistance); //Hearing Distance
        packet.WriteInteger(data.ExtraFlood); //Additional Flood
        packet.WriteBoolean(true);
        packet.WriteInteger(data.WhoCanMute); // who can mute
        packet.WriteInteger(data.WhoCanKick); // who can kick
        packet.WriteInteger(data.WhoCanBan); // who can ban

    }
}
