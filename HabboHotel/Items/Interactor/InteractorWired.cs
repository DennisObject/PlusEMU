using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorWired : IFurniInteractor
{
    public void OnPlace(GameClient session, Item item) { }

    public void OnRemove(GameClient session, Item item)
    {
        //Room Room = Item.GetRoom();
        //Room.GetWiredHandler().RemoveWired(Item);
    }

    public void OnTrigger(GameClient session, Item item, int request, bool hasRights)
    {
        if (session == null || item == null)
            return;
        if (!hasRights)
            return;
        IWiredItem box = null;
        if (!item.GetRoom().GetWired().TryGet(item.Id, out box))
            return;
        if (box is IWiredConfiguredItem configured)
        {
            if (configured.Descriptor.Support == WiredBoxSupport.Implemented)
                session.Send(new WiredConfiguredConfigComposer(configured));
            else
                session.Send(new WiredValidationErrorComposer("This Wired box is not implemented."));
            return;
        }
        item.LegacyDataString = "1";
        item.UpdateState(false, true);
        item.RequestUpdate(2, true);
        if (item.Definition.WiredType == WiredBoxType.AddonRandomEffect)
            return;
        if (item.GetRoom().GetWired().IsTrigger(item))
        {
            var blockedItems = WiredBoxTypeUtility.ContainsBlockedEffect(box, item.GetRoom().GetWired().GetEffects(box));
            session.Send(new WiredTriggeRconfigComposer(box, blockedItems));
        }
        else if (item.GetRoom().GetWired().IsEffect(item))
        {
            var blockedItems = WiredBoxTypeUtility.ContainsBlockedTrigger(box, item.GetRoom().GetWired().GetTriggers(box));
            session.Send(new WiredEffectConfigComposer(box, blockedItems));
        }
        else if (item.GetRoom().GetWired().IsCondition(item))
            session.Send(new WiredConditionConfigComposer(box));
    }


    public void OnWiredTrigger(Item item) { }
}