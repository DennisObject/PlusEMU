using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Settings;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorWired : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item) { }

    public void OnRemove(GameClient? session, Item item)
    {
        //Room Room = Item.GetRoom();
        //Room.GetWiredHandler().RemoveWired(Item);
    }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null) {
            return;
        }

        if (session == null || item == null) {
            return;
        }

        if (!itemRoom.GetWired().Settings.CanInspect(session)) {
            return;
        }

        IWiredItem? box = null;

        if (!itemRoom.GetWired().TryGet(item.Id, out box)) {
            return;
        }

        if (box is not IWiredConfiguredItem configured) {
            session.Send(new WiredValidationErrorComposer("This box has no supported native editor conversion."));

            return;
        }

        try {
            session.Send(new WiredConfiguredConfigComposer(WiredEditorSnapshot.Capture(configured)));
        }
        catch (InvalidDataException) {
            session.Send(new WiredValidationErrorComposer("The saved settings cannot be represented by this native editor."));
        }
    }

    public void OnWiredTrigger(Item item) { }
}
