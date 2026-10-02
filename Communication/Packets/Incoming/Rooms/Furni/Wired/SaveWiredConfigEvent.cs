using System.Data.Common;
using System.Text.Json;
using NLog;
using Plus.Database;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

internal abstract class SaveWiredConfigEvent(IDatabase database) : IPacketEvent
{
    private static readonly ILogger Log = LogManager.GetLogger(nameof(SaveWiredConfigEvent));
    protected abstract WiredBoxCategory Envelope { get; }

    public virtual Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return Task.CompletedTask;
        var room = session.GetHabbo().CurrentRoom;
        if (room == null || !room.CheckRights(session, false, true))
            return Task.CompletedTask;
        try
        {
            var itemId = packet.ReadUInt();
            var selectedItem = room.GetRoomItemHandler().GetItem(itemId);
            if (selectedItem == null || selectedItem.IsTemporary || !room.GetWired().TryGet(itemId, out var box))
                return Task.CompletedTask;
            var actualEnvelope = box is IWiredConfiguredItem configuredItem ? configuredItem.Descriptor.Envelope
                : selectedItem.Definition.InteractionType == InteractionType.WiredTrigger ? WiredBoxCategory.Trigger
                : selectedItem.Definition.InteractionType == InteractionType.WiredCondition ? WiredBoxCategory.Condition
                : WiredBoxCategory.Action;
            if (actualEnvelope != Envelope)
            {
                session.Send(new WiredValidationErrorComposer("The save packet does not match this Wired box."));
                return Task.CompletedTask;
            }
            if (box.Type == WiredBoxType.EffectGiveUserBadge && !session.GetHabbo().Permissions.HasRight("room_item_wired_rewards"))
            {
                session.Send(new WiredValidationErrorComposer("You do not have permission to configure Wired rewards."));
                return Task.CompletedTask;
            }
            var rewardName = box is IWiredConfiguredItem rewardBox ? rewardBox.Descriptor.CanonicalName
                : WiredLegacyEditorProjection.TryGetDescriptor(box, out var rewardDescriptor) ? rewardDescriptor.CanonicalName : null;
            if (rewardName == "wf_act_give_reward" && !session.GetHabbo().Permissions.HasRight("mod_tool"))
            {
                session.Send(new WiredValidationErrorComposer("You do not have permission to configure Wired rewards."));
                return Task.CompletedTask;
            }
            if (box is IWiredConfiguredItem configured)
            {
                if (!WiredLegacyProtocol.TryRead(packet, Envelope, out var proposed))
                {
                    session.Send(new WiredValidationErrorComposer("Invalid Wired settings."));
                    return Task.CompletedTask;
                }
                var store = new WiredConfigurationStore(database);
                if (!WiredConfigurationSave.TrySave(configured, proposed, store, out var error,
                    id => room.GetRoomItemHandler().GetItem(id) != null,
                    publish: room.GetWired().PublishConfigured, prepare: WiredRoomOperations.PrepareSnapshots))
                {
                    session.Send(new WiredValidationErrorComposer(error));
                    return Task.CompletedTask;
                }
            }
            else
            {
                var wired = room.GetWired();
                if (WiredLegacyCustomEditor.IsCustom(box))
                {
                    if (!WiredLegacyProtocol.TryRead(packet, Envelope, out var proposed))
                    {
                        session.Send(new WiredValidationErrorComposer("Invalid custom Wired settings."));
                        return Task.CompletedTask;
                    }
                    if (!WiredLegacyCustomEditor.TryPrepare(box, proposed, WiredLegacyCustomEditor.CreateCandidate,
                        out var candidate, out var customError))
                    {
                        session.Send(new WiredValidationErrorComposer(customError));
                        return Task.CompletedTask;
                    }
                    if (!wired.PublishLegacy(box, candidate!, () => wired.SaveBox(candidate!)))
                    {
                        session.Send(new WiredValidationErrorComposer("This Wired box is no longer attached to the room."));
                        return Task.CompletedTask;
                    }
                }
                else if (WiredLegacyEditorProjection.TryGetDescriptor(box, out var descriptor))
                {
                    if (!WiredLegacyProtocol.TryRead(packet, Envelope, out var proposed))
                    {
                        session.Send(new WiredValidationErrorComposer("Invalid Wired settings."));
                        return Task.CompletedTask;
                    }
                    var candidate = wired.CreateConfiguredBox(selectedItem, descriptor);
                    if (candidate == null)
                    {
                        session.Send(new WiredValidationErrorComposer("This Wired behavior cannot be configured yet."));
                        return Task.CompletedTask;
                    }
                    if (!WiredConfigurationSave.TrySave(candidate, proposed, new WiredConfigurationStore(database), out var error,
                        id => room.GetRoomItemHandler().GetItem(id) != null,
                        publish: (detached, validated, persist) => wired.PublishPromotion(box, detached, validated, persist),
                        prepare: WiredRoomOperations.PrepareSnapshots))
                    {
                        session.Send(new WiredValidationErrorComposer(error));
                        return Task.CompletedTask;
                    }
                }
                else if (!WiredLegacySave.TrySave(box, packet, Envelope, original => wired.GenerateNewBox(original.Item),
                    (original, candidate) => wired.PublishLegacy(original, candidate, () => wired.SaveBox(candidate)),
                    out var error, id => room.GetRoomItemHandler().GetItem(id) != null))
                {
                    session.Send(new WiredValidationErrorComposer(error));
                    return Task.CompletedTask;
                }
            }
            // Octane treats this empty packet as save success. Send it only after persistence succeeds.
            session.Send(new HideWiredConfigComposer());
        }
        catch (Exception error) when (error is ArgumentException or IOException or OverflowException
            or InvalidOperationException or FormatException or DbException or JsonException)
        {
            Log.Warn(error, "Failed to save Wired settings in room {RoomId}", room.Id);
            session.Send(new WiredValidationErrorComposer("Unable to save these Wired settings."));
        }
        return Task.CompletedTask;
    }
}
