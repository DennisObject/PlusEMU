using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Interactor;

// The near-click branch of OnTrigger, callable once the user has reached the approach tile.
public interface IApproachInteractor
{
    int ActionKind { get; }
    bool StartFromApproach(Item item, RoomUser user);
}
