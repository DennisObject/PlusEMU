using Plus.Communication.Packets.Outgoing.Rooms.Avatar;

namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class ActorTickService(Room room)
{
    public bool BeforeMovement(RoomUser actor)
    {
        if (!room.GetRoomUserManager().ValidateMovementActor(actor)) return false;
        actor.IdleTime++; actor.HandleSpamTicks();
        if (!actor.IsBot && !actor.IsAsleep && actor.IdleTime >= 600)
        {
            actor.IsAsleep = true; room.SendPacket(new SleepComposer(actor.VirtualId, true));
        }
        if (actor.CarryItemId > 0 && --actor.CarryTimer <= 0) actor.CarryItem(0);
        if (room.GotFreeze()) room.GetFreeze().CycleUser(actor);
        if (actor.IsRolling && actor.RollerDelay-- <= 0) actor.IsRolling = false;
        return true;
    }
    public void EndCycle()
    {
        var manager = room.GetRoomUserManager();
        var humans = manager.GetUserList().Count(actor => actor.Movement.State == NavState.Active
            && (!actor.IsBot || actor.BotAi == null));
        if (manager.UserCount != humans) manager.UpdateUserCount(humans);
    }
    public void AfterMovement(RoomUser actor)
    {
        if (actor.RidingHorse && !actor.IsBot) actor.ApplyEffect(77);
        if (actor.IsBot && actor.BotAi != null) actor.BotAi.OnTimerTick();
    }
}
