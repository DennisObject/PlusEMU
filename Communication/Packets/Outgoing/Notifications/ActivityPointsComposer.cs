using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Outgoing.Notifications;

public class ActivityPointsComposer : IServerPacket
{
    private readonly IReadOnlyList<KeyValuePair<int, int>> _balances;
    public uint MessageId => ServerPacketHeader.ActivityPointsComposer;

    // Every activity point type the user has, by type; duckets, diamonds and GOTW points are always listed.
    public ActivityPointsComposer(IReadOnlyList<KeyValuePair<int, int>> balances)
    {
        _balances = balances;
    }

    public ActivityPointsComposer(UserCurrencies currencies)
        : this(currencies.Snapshot(ActivityPointType.Duckets, ActivityPointType.Diamonds, ActivityPointType.Gotw)) { }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_balances.Count);

        foreach (var (type, amount) in _balances) {
            packet.WriteInteger(type);
            packet.WriteInteger(amount);
        }
    }
}
