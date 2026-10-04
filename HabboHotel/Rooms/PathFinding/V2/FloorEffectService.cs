using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class FloorEffectService(Room room, Action<GameClient> progressSwim)
{
    public void Apply(RoomUser actor, int x, int y)
    {
        if (actor.IsBot) return;
        var client = actor.GetClient();
        var habbo = client?.GetHabbo();
        if (habbo?.Effects == null) return;
        try
        {
            var value = room.GetGameMap().EffectMap[x, y];
            if (value > 0 && habbo.Effects.CurrentEffect == 0) actor.CurrentItemEffect = ItemEffectType.None;
            var kind = ByteToItemEffectEnum.Parse(value);
            if (kind == actor.CurrentItemEffect) return;
            habbo.Effects.ApplyEffect(EffectId(kind, habbo.Gender));
            actor.CurrentItemEffect = kind;
            if (kind == ItemEffectType.Swim) progressSwim(client!);
        }
        catch { }
    }

    private static int EffectId(ItemEffectType kind, string gender) => kind switch
    {
        ItemEffectType.Iceskates => gender == "M" ? 38 : 39,
        ItemEffectType.Normalskates => gender == "M" ? 55 : 56,
        ItemEffectType.Swim => 29,
        ItemEffectType.SwimLow => 30,
        ItemEffectType.SwimHalloween => 37,
        _ => -1
    };
}
