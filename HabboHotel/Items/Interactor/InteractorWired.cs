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

        if (box is IWiredConfiguredItem configured) {
            if (configured.Descriptor.Support == WiredBoxSupport.Implemented) {
                try {
                    session.Send(new WiredConfiguredConfigComposer(WiredEditorSnapshot.Capture(configured)));
                }
                catch (InvalidDataException) {
                    session.Send(new WiredValidationErrorComposer("The saved settings cannot be represented by this native editor."));
                }
            }
            else {
                session.Send(new WiredValidationErrorComposer("This Wired box is not implemented."));
            }

            return;
        }

        var legacyJoin = itemRoom.GetWired().CaptureLegacyJoin(box,
            () => ReferenceEquals(session.GetHabbo().CurrentRoom, itemRoom)
                && itemRoom.GetWired().Settings.CanInspect(session));

        if (legacyJoin != null) {
            session.Send(new WiredConfiguredConfigComposer(new(item.Id, item.Definition.SpriteId,
                legacyJoin.Descriptor, new(), WiredConfigurationLimits.SelectedItems, [])
            { Native = legacyJoin.Native }));

            return;
        }

        // Unmapped legacy boxes keep executing. Their old editor body is not a canonical fallback.
        session.Send(new WiredValidationErrorComposer("This box has no supported native editor conversion."));

        return;

    }

    public void OnWiredTrigger(Item item) { }
}
