using System.Buffers.Binary;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal sealed class SendRoomInviteEvent(IMessengerSocialMutationService social) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!TryReadRequest(packet, out var request))
        {
            return Task.CompletedTask;
        }

        return social.SendRoomInvites(session, request);
    }

    internal static bool TryReadRequest(IIncomingPacket packet, out RoomInvitationRequest request)
    {
        request = default;

        if (packet.Buffer.Length < sizeof(int))
        {
            return false;
        }

        var count = packet.ReadInt();

        if (count is < 0 or > 500 || packet.Buffer.Length < count * sizeof(int) + sizeof(ushort))
        {
            return false;
        }

        var recipients = new List<int>(Math.Min(count, 100));

        for (var index = 0; index < count; index++)
        {
            var userId = packet.ReadInt();

            if (index < 100)
            {
                recipients.Add(userId);
            }
        }

        var remaining = packet.Buffer.Span;
        var length = BinaryPrimitives.ReadUInt16BigEndian(remaining);

        if (remaining.Length < sizeof(ushort) + length)
        {
            return false;
        }

        request = new RoomInvitationRequest(recipients, packet.ReadString());

        return true;
    }
}
