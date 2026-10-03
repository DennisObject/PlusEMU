using Plus.Communication.Packets.Outgoing.Rooms.Avatar;

namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class ActorTickService(Room room)
{
    public void BeforeMovement(RoomUser actor)
    {
        actor.IdleTime++; actor.HandleSpamTicks();
        if (!actor.IsBot && !actor.IsAsleep && actor.IdleTime >= 600)
        {
            actor.IsAsleep = true; room.SendPacket(new SleepComposer(actor, true));
        }
        if (actor.CarryItemId > 0 && --actor.CarryTimer <= 0) actor.CarryItem(0);
        if (room.GotFreeze()) room.GetFreeze().CycleUser(actor);
        if (actor.IsRolling && actor.RollerDelay-- <= 0) actor.IsRolling = false;
    }
    public void AfterMovement(RoomUser actor)
    {
        if (actor.RidingHorse && !actor.IsBot) actor.ApplyEffect(77);
        if (actor.IsBot && actor.BotAi != null) actor.BotAi.OnTimerTick();
    }
}
