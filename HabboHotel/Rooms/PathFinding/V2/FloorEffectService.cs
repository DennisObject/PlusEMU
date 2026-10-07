using Plus.HabboHotel.Items;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class FloorEffectService(Room room, Action<GameClient> progressSwim)
{
    public void Apply(RoomUser actor, int x, int y, SurfaceRef? surface = null)
    {
        if (actor.IsBot) {
            return;
        }

        var client = actor.GetClient();
        var habbo = client?.GetHabbo();

        if (habbo?.Effects == null) {
            return;
        }

        try {
            var value = EffectValue(x, y, surface, actor.Movement.SupportZ);

            Item? ice = null;
            if (value == 3) {
                var grid = room.GetGameMap().Navigation?.Grid;
                var slot = SurfaceContacts.ContactSlot(grid, x, y, surface, actor.Movement.SupportZ);
                ice = SurfaceContacts.Filter(grid, x, y, slot, room.GetGameMap().GetCoordinatedItems(new(x, y)))
                    .OrderByDescending(item => item.TotalHeight).FirstOrDefault(item => item.Definition.InteractionType == InteractionType.IceSkates);
            }

            var currentRoom = habbo.CurrentRoom;
            var iceEffect = room.UpdateIceTag(actor, ice);
            if (!ReferenceEquals(habbo.CurrentRoom, currentRoom)) {
                return;
            }
            if (value > 0 && habbo.Effects.CurrentEffect == 0) {
                actor.CurrentItemEffect = ItemEffectType.None;
            }

            var kind = ByteToItemEffectEnum.Parse(value);

            if (kind == actor.CurrentItemEffect && (iceEffect < 0 || habbo.Effects.CurrentEffect == iceEffect)) {
                return;
            }

            habbo.Effects.ApplyEffect(kind == ItemEffectType.Iceskates && iceEffect >= 0 ? iceEffect : EffectId(kind, habbo.Gender));
            actor.CurrentItemEffect = kind;

            if (kind == ItemEffectType.Swim) {
                progressSwim(client!);
            }
        }
        catch { }
    }

    // K=1 reads the legacy tile map. A layered surface takes the effect of its highest owned item,
    // so skates under a deck do not apply on the deck; a walk magic surface has none (as legacy).
    private byte EffectValue(int x, int y, SurfaceRef? surface, double z)
    {
        var map = room.GetGameMap();
        var grid = map.Navigation?.Grid;
        var slot = SurfaceContacts.ContactSlot(grid, x, y, surface, z);

        if (slot < 0) {
            return map.EffectMap[x, y];
        }

        if (grid!.Kind[slot] == SurfaceKind.WalkMagic) {
            return 0;
        }

        var top = SurfaceContacts.Filter(grid, x, y, slot, map.GetAllRoomItemForSquare(x, y)).MaxBy(item => item.TotalHeight);

        return top != null ? ItemEffect(top.Definition.InteractionType)
            : map.Model.SqState[x, y] == SquareState.Pool ? (byte)6 : (byte)0;
    }

    // The same item→effect codes the legacy map writes (Gamemap.AddItemToMap); the legacy code stays untouched.
    private static byte ItemEffect(InteractionType interaction) => interaction switch
    {
        InteractionType.Pool => 1,
        InteractionType.NormalSkates => 2,
        InteractionType.IceSkates => 3,
        InteractionType.Lowpool => 4,
        InteractionType.Haloweenpool => 5,
        _ => 0
    };

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
