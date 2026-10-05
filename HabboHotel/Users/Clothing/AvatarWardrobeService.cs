using Plus.Communication.Packets.Outgoing.Avatar;
using Plus.Core.FigureData;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users.UserData;

namespace Plus.HabboHotel.Users.Clothing;

public interface IAvatarWardrobeService
{
    Task ShowWardrobe(GameClient session);
    void SaveOutfit(Habbo habbo, int slotId, string look, string gender);
}

public sealed class AvatarWardrobeService(IFigureDataManager figures, IAvatarWardrobeStore store, IUserDataFactory userData) : IAvatarWardrobeService
{
    public async Task ShowWardrobe(GameClient session)
    {
        var userId = session.GetHabbo().Id;
        if (!await userData.HabboExists(userId)) return;
        var slots = await store.LoadSlots(userId);
        session.Send(new WardrobeComposer(new WardrobeSnapshot(slots)));
    }

    public void SaveOutfit(Habbo habbo, int slotId, string look, string gender)
    {
        var processed = figures.ProcessFigure(look, gender, habbo.Clothing.GetClothingParts, ClubAccess.LevelFor(habbo.Access));
        store.SaveSlot(habbo.Id, slotId, processed, gender.ToUpper());
    }
}
