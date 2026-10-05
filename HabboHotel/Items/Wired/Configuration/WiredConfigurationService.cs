using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.Core.FigureData;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Subscriptions;

namespace Plus.HabboHotel.Items.Wired.Configuration;

public sealed record WiredConfigurationSaveRequest(uint ItemId, WiredBoxCategory Envelope, WiredConfiguration Configuration);

public interface IWiredConfigurationService
{
    void Save(GameClient session, WiredConfigurationSaveRequest request);
}

public sealed class WiredConfigurationService(
    IWiredConfigurationStore store,
    IFigureDataManager figures,
    ILogger<WiredConfigurationService> logger) : IWiredConfigurationService
{
    public void Save(GameClient session, WiredConfigurationSaveRequest request)
    {
        var habbo = session.GetHabbo();
        if (!habbo.InRoom || habbo.CurrentRoom is not { } room || !room.GetWired().Settings.CanModify(session))
            return;
        try
        {
            var selectedItem = room.GetRoomItemHandler().GetItem(request.ItemId);
            if (selectedItem == null || selectedItem.IsTemporary || !room.GetWired().TryGet(request.ItemId, out var box))
                return;
            var actualEnvelope = box is IWiredConfiguredItem configuredItem ? configuredItem.Descriptor.Envelope
                : selectedItem.Definition.InteractionType == InteractionType.WiredTrigger ? WiredBoxCategory.Trigger
                : selectedItem.Definition.InteractionType == InteractionType.WiredCondition ? WiredBoxCategory.Condition
                : WiredBoxCategory.Action;
            if (actualEnvelope != request.Envelope)
            {
                session.Send(new WiredValidationErrorComposer("The save packet does not match this Wired box."));
                return;
            }
            if (box.Type == WiredBoxType.EffectGiveUserBadge && !habbo.Access.Can(PermissionKeys.RoomItemWiredRewards))
            {
                session.Send(new WiredValidationErrorComposer("You do not have permission to configure Wired rewards."));
                return;
            }
            var rewardName = box is IWiredConfiguredItem rewardBox ? rewardBox.Descriptor.CanonicalName
                : WiredLegacyEditorProjection.TryGetDescriptor(box, out var rewardDescriptor) ? rewardDescriptor.CanonicalName : null;
            if (rewardName == "wf_act_give_reward" && !habbo.Access.Can(PermissionKeys.ModerationTool))
            {
                session.Send(new WiredValidationErrorComposer("You do not have permission to configure Wired rewards."));
                return;
            }

            if (!TrySave(room.GetWired(), selectedItem, box, session, request.Envelope, request.Configuration, out var error))
            {
                session.Send(new WiredValidationErrorComposer(error));
                return;
            }
            session.Send(new HideWiredConfigComposer());
        }
        catch (Exception error) when (error is ArgumentException or IOException or OverflowException
            or InvalidOperationException or FormatException or DbException or JsonException)
        {
            logger.LogWarning(error, "Failed to save Wired settings in room {RoomId}", room.Id);
            session.Send(new WiredValidationErrorComposer("Unable to save these Wired settings."));
        }
    }

    private bool TrySave(Rooms.Instance.WiredComponent wired, Item selectedItem, IWiredItem box, GameClient session,
        WiredBoxCategory envelope, WiredConfiguration proposed, out string error)
    {
        var room = box.Instance;
        if (box is IWiredConfiguredItem configured)
        {
            proposed = PreserveAdvancedConfiguration(configured, proposed);
            if (configured.Descriptor.CanonicalName == "wf_act_bot_clothes")
                proposed = proposed with { Text = ValidateBotFigure(proposed.Text, session) };
            return WiredConfigurationSave.TrySave(configured, proposed, store, out error,
                id => room.GetRoomItemHandler().GetItem(id) != null,
                publish: wired.PublishConfigured, prepare: WiredRoomOperations.PrepareSnapshots,
                isTemporaryInRoom: id => room.GetRoomItemHandler().GetItem(id)?.IsTemporary == true);
        }
        if (WiredLegacyCustomEditor.IsCustom(box))
        {
            if (!WiredLegacyCustomEditor.TryPrepare(box, proposed, WiredLegacyCustomEditor.CreateCandidate,
                out var candidate, out error))
                return false;
            if (wired.PublishLegacy(box, candidate!, () => wired.SaveBox(candidate!)))
                return true;
            error = "This Wired box is no longer attached to the room.";
            return false;
        }
        if (WiredLegacyEditorProjection.TryGetDescriptor(box, out var descriptor))
        {
            if (descriptor.CanonicalName == "wf_act_bot_clothes")
                proposed = proposed with { Text = ValidateBotFigure(proposed.Text, session) };
            var candidate = wired.CreateConfiguredBox(selectedItem, descriptor);
            if (candidate == null)
            {
                error = "This Wired behavior cannot be configured yet.";
                return false;
            }
            return WiredConfigurationSave.TrySave(candidate, proposed, store, out error,
                id => room.GetRoomItemHandler().GetItem(id) != null,
                publish: (detached, validated, persist) => wired.PublishPromotion(box, detached, validated, persist),
                prepare: WiredRoomOperations.PrepareSnapshots,
                isTemporaryInRoom: id => room.GetRoomItemHandler().GetItem(id)?.IsTemporary == true);
        }
        return WiredLegacySave.TrySave(box, proposed, envelope,
            original => wired.GenerateNewBox(original.Item),
            (original, candidate) =>
            {
                if (candidate.Type == WiredBoxType.EffectBotChangesClothesBox)
                    candidate.StringData = ValidateBotFigure(candidate.StringData, session);
                return wired.PublishLegacy(original, candidate, () => wired.SaveBox(candidate));
            }, out error, id => room.GetRoomItemHandler().GetItem(id) is { IsTemporary: false });
    }

    private string ValidateBotFigure(string data, GameClient session)
    {
        var fields = data.Split('\t', 2);
        if (fields.Length == 2) fields[1] = fields[1].TrimEnd('.');
        if (fields.Length != 2 || !WiredBotActions.FigureWellFormed(fields[1]))
            throw new ArgumentException("Invalid bot figure.");
        var habbo = session.GetHabbo();
        var validated = figures.ProcessFigure(fields[1], habbo.Gender, habbo.Clothing.GetClothingParts, ClubAccess.LevelFor(habbo.Access));
        return fields[0] + "\t" + validated.TrimEnd('.');
    }

    private static WiredConfiguration PreserveAdvancedConfiguration(IWiredConfiguredItem box, WiredConfiguration proposed)
    {
        var saved = box.Configuration;
        proposed = proposed with { ScoreQuotaPerGame = saved.ScoreQuotaPerGame };
        if (box.Descriptor.CanonicalName == "wf_act_place_furni" && saved.TemporaryPlacement != null)
            proposed = proposed with { TemporaryPlacement = saved.TemporaryPlacement, Snapshots = saved.Snapshots,
                SecondarySelectedItems = saved.SecondarySelectedItems, FurniSources = saved.FurniSources,
                UserSources = saved.UserSources, VariableIds = saved.VariableIds };
        return proposed;
    }
}
