using Plus.HabboHotel.Rooms.AI;
using Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Chat.Pets.Locale;

namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class HorseMountService(Room room, RoomNavigation navigation, MovementContext context)
{
    internal void Ride(RoomUser rider, RoomUser horse, bool mount, IPetLocale locale)
    {
        var lifetime = horse.Movement.LifetimeId;
        var sequence = horse.Movement.Commands.Read()?.Sequence ?? 0;
        navigation.RunOwner(rider, (actor, discardThrough) =>
        {
            if (!Attached(horse, lifetime)) return;
            if (mount) TryMount(actor, horse, discardThrough, sequence, locale);
            else TryDismount(actor, horse, discardThrough, sequence);
            room.SendPacket(new PetHorseFigureInformationComposer(PetAppearanceSnapshots.Horse(horse)));
        });
    }

    internal void DetachPickedUpHorse(RoomUser horse)
    {
        var rider = room.GetRoomUserManager().GetRoomUserByVirtualId(horse.HorseId);
        if (rider == null) return;
        var lifetime = horse.Movement.LifetimeId;
        navigation.RunOwner(rider, (actor, discardThrough) =>
        {
            if (horse.Movement.LifetimeId != lifetime || actor.HorseId != horse.VirtualId) return;
            navigation.CancelThrough(actor, discardThrough);
            ClearGroup(actor, horse);
            actor.ApplyEffect(-1);
            PostureService.Apply(room, navigation.Grid, actor);
            context.RefreshMembership(actor);
            ResumeDismounted(actor, 1, discardThrough);
        });
    }

    private bool Attached(RoomUser horse, long lifetime) => horse.Movement.LifetimeId == lifetime
        && horse.Movement.State == NavState.Active
        && ReferenceEquals(room.GetRoomUserManager().GetRoomUserByVirtualId(horse.VirtualId), horse);

    private void TryMount(RoomUser rider, RoomUser horse, long riderSequence, long horseSequence, IPetLocale locale)
    {
        if (horse.RidingHorse)
        {
            var speech = locale.GetValue("pet.alreadymounted");
            horse.Chat(speech[Random.Shared.Next(speech.Length)]);
        }
        else if (rider.RidingHorse) rider.GetClient()?.SendNotification("You are already riding a horse!");
        else Mount(rider, horse, riderSequence, horseSequence);
    }

    private void Mount(RoomUser rider, RoomUser horse, long riderSequence, long horseSequence)
    {
        var origin = (horse.X, horse.Y, horse.Z);
        var riderOrigin = (rider.X, rider.Y, rider.Z);
        var supportZ = room.GetGameMap().SqAbsoluteHeight(rider.X, rider.Y);
        horse.Statusses.Clear();
        rider.RidingHorse = horse.RidingHorse = true;
        rider.HorseId = horse.VirtualId; horse.HorseId = rider.VirtualId;
        navigation.ForcePlaceThrough(horse, rider.X, rider.Y, supportZ, ForceResolution.Highest, horseSequence);
        navigation.ForcePlaceThrough(rider, rider.X, rider.Y, supportZ, ForceResolution.Highest, riderSequence);
        rider.RotHead = horse.RotHead; rider.RotBody = horse.RotBody;
        rider.ApplyEffect(77); rider.UpdateNeeded = horse.UpdateNeeded = true;
        room.SendPacket(new SlideObjectBundleComposer(origin.X, origin.Y, origin.Z,
            horse.X, horse.Y, horse.Z, 0, horse.VirtualId, 0));
        room.SendPacket(new SlideObjectBundleComposer(riderOrigin.X, riderOrigin.Y, riderOrigin.Z,
            rider.X, rider.Y, rider.Z, 0, rider.VirtualId, 0));
    }

    private void TryDismount(RoomUser rider, RoomUser horse, long riderSequence, long horseSequence)
    {
        if (horse.HorseId != rider.VirtualId)
        {
            rider.GetClient()?.SendNotification("Could not dismount this horse - You are not riding it!");
            return;
        }
        navigation.CancelThrough(rider, riderSequence); navigation.CancelThrough(horse, horseSequence);
        foreach (var status in new[] { "sit", "lay", "snf", "eat", "ded", "jmp" }) horse.Statusses.Remove(status);
        ClearGroup(rider, horse);
        rider.ApplyEffect(-1);
        PostureService.Apply(room, navigation.Grid, rider);
        context.RefreshMembership(rider); context.RefreshMembership(horse);
        ResumeDismounted(rider, 2, riderSequence);
    }

    private static void ClearGroup(RoomUser rider, RoomUser horse)
    {
        rider.RidingHorse = horse.RidingHorse = false;
        rider.HorseId = horse.HorseId = 0;
        rider.UpdateNeeded = horse.UpdateNeeded = true;
    }

    private static void ResumeDismounted(RoomUser rider, int offset, long discardThrough)
    {
        if ((rider.Movement.Commands.Read()?.Sequence ?? 0) <= discardThrough)
            rider.MoveTo(rider.X + offset, rider.Y + offset);
    }
}
