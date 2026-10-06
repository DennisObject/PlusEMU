using Plus.Communication.Packets.Outgoing.Inventory.Badges;
using Plus.Communication.Packets.Outgoing.Inventory.Bots;
using Plus.Communication.Packets.Outgoing.Inventory.Pets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Users.Inventory.Bots;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Users.Inventory;

[Singleton]
public interface IInventoryShowcaseService
{
    void ShowBots(GameClient session);
    void ShowPets(GameClient session);
    void ShowBadges(GameClient session);
}

public sealed class InventoryShowcaseService : IInventoryShowcaseService
{
    public void ShowBots(GameClient session)
    {
        var inventory = session.GetHabbo().Inventory;

        if (inventory == null) {
            return;
        }

        session.Send(new BotInventoryComposer(BotInventorySnapshot.Capture(inventory.Bots.Bots.Values)));
    }

    public void ShowPets(GameClient session)
    {
        var inventory = session.GetHabbo().Inventory;

        if (inventory == null) {
            return;
        }

        session.Send(new PetInventoryComposer(PetAppearanceSnapshots.Inventory(inventory.Pets.Pets.Values)));
    }

    public void ShowBadges(GameClient session) => session.Send(new BadgesComposer(
        BadgeInventorySnapshot.Capture(session.GetHabbo().Inventory.Badges.Badges.Values)));
}
