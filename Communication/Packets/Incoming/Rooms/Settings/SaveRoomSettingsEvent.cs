using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Settings;

internal sealed class SaveRoomSettingsEvent(IRoomSettingsService settings) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var roomId = packet.ReadUInt();
        var name = packet.ReadString();
        var description = packet.ReadString();
        var access = packet.ReadInt();
        var password = packet.ReadString();
        var maxUsers = packet.ReadInt();
        var category = packet.ReadInt();
        var tagCount = packet.ReadInt();
        var tags = ImmutableArray.CreateBuilder<string>();

        for (var index = 0; index < tagCount; index++) {
            tags.Add(packet.ReadString());
        }

        settings.Save(session, new(roomId, name, description, access, password, maxUsers, category, tags.ToImmutable(),
            packet.ReadInt(), packet.ReadBool(), packet.ReadBool(), packet.ReadBool(), packet.ReadBool(),
            packet.ReadInt(), packet.ReadInt(), packet.ReadInt(), packet.ReadInt(), packet.ReadInt(),
            packet.ReadInt(), packet.ReadInt(), packet.ReadInt(), packet.ReadInt(), packet.ReadInt()));

        return Task.CompletedTask;
    }
}
