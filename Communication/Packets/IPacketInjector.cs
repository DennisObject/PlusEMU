using Plus.Communication.Flash;
using Plus.HabboHotel.GameClients;
using Plus.Utilities.DependencyInjection;

namespace Plus.Communication.Packets;

[Singleton]
public interface IPacketInjector
{
    uint MessageId { get; }
}

public interface IIncomingPacketInjector : IPacketInjector
{
    void ModifyIncomingPacket(IGameServer server, GameClient session, IIncomingPacket packet);
}

public interface IOutgoingPacketInjector : IPacketInjector
{
    void ModifyOutgoingPacket(IGameServer server, GameClient session, IOutgoingPacket packet);
}
