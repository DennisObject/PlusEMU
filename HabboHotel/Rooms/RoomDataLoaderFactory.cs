using Plus.Database;
using Plus.HabboHotel.Groups;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

[Singleton]
public interface IRoomDataLoaderFactory
{
    IRoomDataLoader Create(IRoomManager rooms);
}

public sealed class RoomDataLoaderFactory(IDatabase database, IGroupManager groups,
    IRoomPromotionLoader promotions) : IRoomDataLoaderFactory
{
    public IRoomDataLoader Create(IRoomManager rooms) => new RoomDataLoader(database, rooms, groups, promotions);
}
