using System.Globalization;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public class ModeratorUserInfoComposer(ModerationUserData user, bool online, DateTimeOffset now) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ModeratorUserInfoComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(user.Id);
        packet.WriteString(user.Username);
        packet.WriteString(user.Look);
        packet.WriteInteger(MinutesSince(user.AccountCreatedAt));
        packet.WriteInteger(MinutesSince(user.LastOnlineAt));
        packet.WriteBoolean(online);
        packet.WriteInteger(user.HelpRequests);
        packet.WriteInteger(user.AbusiveHelpRequests);
        packet.WriteInteger(user.Cautions);
        packet.WriteInteger(user.Bans);
        packet.WriteInteger(user.TradingLockCount);
        packet.WriteString(user.TradingLockExpiresAt?.UtcDateTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) ?? "0");
        packet.WriteString("");
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteString(user.Mail);
        packet.WriteString("");
    }

    private int MinutesSince(DateTimeOffset? timestamp) => timestamp == null ? 0 :
        (int)Math.Clamp(Math.Ceiling((now - timestamp.Value).TotalMinutes), 0, int.MaxValue);
}
