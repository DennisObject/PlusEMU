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

public sealed record WiredConfigurationSaveRequest(uint ItemId, WiredBoxCategory Envelope, WiredConfiguration Configuration,
    WiredNativeEditorConfiguration? Native = null);

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

        if (!habbo.InRoom || habbo.CurrentRoom is not { } room || !room.GetWired().Settings.CanModify(session)) {
            return;
        }

        try {
            var selectedItem = room.GetRoomItemHandler().GetItem(request.ItemId);

            if (selectedItem is { IsTemporary: false } && Plus.HabboHotel.Items.Wired.Chests.WiredChestFurniture.IsContract(selectedItem.Definition)) {
                if (request.Native != null) {
                    session.Send(new WiredValidationErrorComposer("This contract has no supported native editor conversion."));

                    return;
                }

                var saved = false;
                room.GetWired().WithChests(module => saved = request.Envelope == WiredBoxCategory.Action
                    && Plus.HabboHotel.Items.Wired.Chests.WiredChestContractEditor.TrySave(module, selectedItem, request.Configuration));
                session.Send(saved ? new HideWiredConfigComposer() : new WiredValidationErrorComposer("Invalid contract requirements."));

                return;
            }

            if (selectedItem == null || selectedItem.IsTemporary || !room.GetWired().TryGet(request.ItemId, out var box)) {
                return;
            }

            var actualEnvelope = box is IWiredConfiguredItem configuredItem ? configuredItem.Descriptor.Envelope
                : selectedItem.Definition.InteractionType == InteractionType.WiredTrigger ? WiredBoxCategory.Trigger
                : selectedItem.Definition.InteractionType == InteractionType.WiredCondition ? WiredBoxCategory.Condition
                : WiredBoxCategory.Action;

            if (actualEnvelope != request.Envelope) {
                session.Send(new WiredValidationErrorComposer("The save packet does not match this Wired box."));

                return;
            }

            if (box.Type == WiredBoxType.EffectGiveUserBadge && !habbo.Access.Can(PermissionKeys.RoomItemWiredRewards)) {
                session.Send(new WiredValidationErrorComposer("You do not have permission to configure Wired rewards."));

                return;
            }

            var rewardName = box is IWiredConfiguredItem rewardBox ? rewardBox.Descriptor.CanonicalName
                : WiredLegacyEditorProjection.TryGetDescriptor(box, out var rewardDescriptor) ? rewardDescriptor.CanonicalName : null;

            if (rewardName == "wf_act_give_reward" && !habbo.Access.Can(PermissionKeys.ModerationTool)) {
                session.Send(new WiredValidationErrorComposer("You do not have permission to configure Wired rewards."));

                return;
            }

            if (box is not IWiredConfiguredItem && request.Native is { } pristineNative
                && WiredLegacyEditorProjection.TryGetDescriptor(box, out var pristineDescriptor)) {
                var proof = room.GetWired().CapturePristineCard(box,
                    pristineNative with { NativeCode = WiredNativeEditorProjection.Code(pristineDescriptor.CanonicalName) },
                    () => ReferenceEquals(habbo.CurrentRoom, room) && room.GetWired().Settings.CanModify(session));

                if (proof != null) {
                    SavePristineCard(session, proof);

                    return;
                }
            }

            if (request.Native is { } freshNative) {
                var fresh = room.GetWired().CaptureFreshDirection(box, freshNative with { NativeCode = 13 },
                    () => ReferenceEquals(habbo.CurrentRoom, room) && room.GetWired().Settings.CanModify(session));

                if (fresh != null) {
                    SaveFreshDirection(session, fresh);

                    return;
                }
            }

            if (box is not IWiredConfiguredItem && request.Native is { } legacyNative) {
                var rotateProof = room.GetWired().CaptureLegacyRotate(box,
                    () => ReferenceEquals(habbo.CurrentRoom, room) && room.GetWired().Settings.CanModify(session));

                if (rotateProof != null) {
                    SaveLegacyRotate(session, rotateProof, legacyNative);

                    return;
                }

                var proof = room.GetWired().CaptureLegacyJoin(box,
                    () => ReferenceEquals(habbo.CurrentRoom, room) && room.GetWired().Settings.CanModify(session));

                var saysProof = room.GetWired().CaptureLegacySays(box,
                    () => ReferenceEquals(habbo.CurrentRoom, room) && room.GetWired().Settings.CanModify(session));

                if (saysProof != null) {
                    SaveLegacySays(session, saysProof, legacyNative);

                    return;
                }

                if (proof != null) {
                    SaveLegacyJoin(session, proof, legacyNative);

                    return;
                }
            }

            if (request.Native is not { } native || box is not IWiredConfiguredItem nativeBox
                || !WiredNativeEditorProjection.Supports(nativeBox.Descriptor.CanonicalName)) {
                session.Send(new WiredValidationErrorComposer("This box has no supported native editor conversion."));

                return;
            }

            native = native with { NativeCode = WiredNativeEditorProjection.Code(nativeBox.Descriptor.CanonicalName) };
            var admission = room.GetWired().TryAdmitUnchangedNativeSave(nativeBox, native,
                () => habbo.CurrentRoom == room && room.GetWired().Settings.CanModify(session));

            if (admission == WiredNativeSaveAdmission.Unchanged) {
                session.Send(new HideWiredConfigComposer());

                return;
            }

            if (admission == WiredNativeSaveAdmission.Refused) {
                session.Send(new WiredValidationErrorComposer("The saved settings cannot be represented by this native editor."));

                return;
            }

            var current = nativeBox.Configuration;

            if (!WiredNativeEditorProjection.TryProject(selectedItem, nativeBox.Descriptor, current, out var projected)) {
                session.Send(new WiredValidationErrorComposer("Unable to project the saved settings."));

                return;
            }

            native = native with { SavedState = projected.SavedState, DormantLegacy = projected.DormantLegacy };

            var error = "Invalid native Wired settings.";

            if (!WiredNativeEditorProjection.TryCompile(selectedItem.Id, nativeBox.Descriptor, native, out var runtime)
                || !WiredConfigurationSave.TrySave(nativeBox, runtime, store, out error,
                    id => room.GetRoomItemHandler().GetItem(id) != null,
                    (original, validated, persist) => room.GetWired().PublishConfigured(original, validated, () =>
                    {
                        if (habbo.CurrentRoom != room || !room.GetWired().Settings.CanModify(session)
                            || !ReferenceEquals(current, original.Configuration)) {
                            throw new InvalidOperationException("The editor admission is no longer current.");
                        }

                        persist();
                    }), isTemporaryInRoom: id => room.GetRoomItemHandler().GetItem(id)?.IsTemporary == true)) {
                session.Send(new WiredValidationErrorComposer(error));

                return;
            }

            session.Send(new HideWiredConfigComposer());
        }
        catch (Exception error) when (error is ArgumentException or IOException or OverflowException
            or InvalidOperationException or FormatException or DbException or JsonException) {
            logger.LogWarning(error, "Failed to save Wired settings in room {RoomId}", room.Id);
            session.Send(new WiredValidationErrorComposer("Unable to save these Wired settings."));
        }
    }

    private void SavePristineCard(GameClient session, PristineCardSnapshot proof)
    {
        var wired = proof.Room.GetWired();
        bool CanModify() => ReferenceEquals(session.GetHabbo().CurrentRoom, proof.Room) && wired.Settings.CanModify(session);
        var error = "Invalid pristine native card settings.";
        var candidate = wired.CreateConfiguredBox(proof.Item, proof.Descriptor);

        if (proof.Request == null || candidate == null
            || !WiredNativeEditorProjection.TryCompile(proof.Item.Id, proof.Descriptor, proof.Request, out var runtime)
            || !WiredConfigurationSave.TrySave(candidate, runtime, store, out error,
                id => proof.Room.GetRoomItemHandler().GetItem(id) != null,
                (detached, validated, persist) => wired.PublishPristineCard(proof, detached, validated, CanModify, persist),
                isTemporaryInRoom: id => proof.Room.GetRoomItemHandler().GetItem(id)?.IsTemporary == true)) {
            session.Send(new WiredValidationErrorComposer(error));

            return;
        }

        session.Send(new HideWiredConfigComposer());
    }

    private void SaveFreshDirection(GameClient session, FreshDirectionSnapshot proof)
    {
        var wired = proof.Room.GetWired();
        bool CanModify() => ReferenceEquals(session.GetHabbo().CurrentRoom, proof.Room) && wired.Settings.CanModify(session);
        var error = "Invalid fresh direction settings.";

        if (proof.Request == null || !WiredNativeEditorProjection.TryCompile(proof.Item.Id, proof.Descriptor, proof.Request, out var runtime)
            || !WiredConfigurationSave.TrySave(proof.Box, runtime, store, out error,
                id => proof.Room.GetRoomItemHandler().GetItem(id) != null,
                (original, validated, persist) => wired.PublishFreshDirection(proof, validated, CanModify, persist),
                isTemporaryInRoom: id => proof.Room.GetRoomItemHandler().GetItem(id)?.IsTemporary == true)) {
            session.Send(new WiredValidationErrorComposer(error));

            return;
        }

        session.Send(new HideWiredConfigComposer());
    }

    private void SaveLegacyRotate(GameClient session, LegacyRotateSnapshot proof, WiredNativeEditorConfiguration request)
    {
        var wired = proof.Room.GetWired();
        bool CanModify() => ReferenceEquals(session.GetHabbo().CurrentRoom, proof.Room) && wired.Settings.CanModify(session);
        var native = request with { NativeCode = 4, SavedState = proof.Native.SavedState, DormantLegacy = proof.Native.DormantLegacy };
        var error = "The captured legacy rotate settings cannot be saved.";
        var candidate = wired.CreateConfiguredBox(proof.Item, proof.Descriptor);

        if (candidate == null || !WiredNativeEditorProjection.TryCompile(proof.Item.Id, proof.Descriptor, native, out var runtime)
            || !WiredConfigurationSave.TrySave(candidate, runtime, store, out error,
                id => proof.Room.GetRoomItemHandler().GetItem(id) != null,
                (detached, validated, persist) => wired.PublishLegacyRotate(proof, detached, validated, CanModify, persist),
                isTemporaryInRoom: id => proof.Room.GetRoomItemHandler().GetItem(id)?.IsTemporary == true)) {
            session.Send(new WiredValidationErrorComposer(error));

            return;
        }

        session.Send(new HideWiredConfigComposer());
    }

    private void SaveLegacySays(GameClient session, LegacySaysSnapshot proof, WiredNativeEditorConfiguration request)
    {
        var wired = proof.Room.GetWired();
        bool CanModify() => ReferenceEquals(session.GetHabbo().CurrentRoom, proof.Room) && wired.Settings.CanModify(session);
        var native = request with { NativeCode = 0, SavedState = proof.Native.SavedState, DormantLegacy = proof.Native.DormantLegacy };
        var error = "The captured legacy Says settings cannot be saved.";
        var candidate = wired.CreateConfiguredBox(proof.Item, proof.Descriptor);

        if (wired.AdmitLegacySays(proof, CanModify) == WiredNativeSaveAdmission.Refused || candidate == null
            || !WiredNativeEditorProjection.TryCompile(proof.Item.Id, proof.Descriptor, native, out var runtime)
            || !WiredConfigurationSave.TrySave(candidate, runtime, store, out error,
                id => proof.Room.GetRoomItemHandler().GetItem(id) != null,
                (detached, validated, persist) => wired.PublishLegacySays(proof, detached, validated, CanModify, persist),
                isTemporaryInRoom: id => proof.Room.GetRoomItemHandler().GetItem(id)?.IsTemporary == true)) {
            session.Send(new WiredValidationErrorComposer(error));

            return;
        }

        session.Send(new HideWiredConfigComposer());
    }

    private void SaveLegacyJoin(GameClient session, LegacyJoinSnapshot proof, WiredNativeEditorConfiguration request)
    {
        var wired = proof.Room.GetWired();
        bool CanModify() => ReferenceEquals(session.GetHabbo().CurrentRoom, proof.Room) && wired.Settings.CanModify(session);
        var native = request with { NativeCode = WiredNativeEditorProjection.Code(proof.Descriptor.CanonicalName) };
        var admission = wired.AdmitLegacyJoin(proof, native, CanModify);

        if (admission == WiredNativeSaveAdmission.Unchanged) {
            session.Send(new HideWiredConfigComposer());

            return;
        }

        var error = "The captured legacy Join settings cannot be saved.";
        var candidate = wired.CreateConfiguredBox(proof.Item, proof.Descriptor);
        native = native with { SavedState = proof.Native.SavedState, DormantLegacy = proof.Native.DormantLegacy };

        if (admission == WiredNativeSaveAdmission.Refused || candidate == null
            || !WiredNativeEditorProjection.TryCompile(proof.Item.Id, proof.Descriptor, native, out var runtime)
            || !WiredConfigurationSave.TrySave(candidate, runtime, store, out error,
                id => proof.Room.GetRoomItemHandler().GetItem(id) != null,
                (detached, validated, persist) => wired.PublishLegacyJoin(proof, detached, validated, CanModify, persist),
                isTemporaryInRoom: id => proof.Room.GetRoomItemHandler().GetItem(id)?.IsTemporary == true)) {
            session.Send(new WiredValidationErrorComposer(error));

            return;
        }

        session.Send(new HideWiredConfigComposer());
    }

    private bool TrySave(Rooms.Instance.WiredComponent wired, Item selectedItem, IWiredItem box, GameClient session,
        WiredBoxCategory envelope, WiredConfiguration proposed, out string error)
    {
        var room = box.Instance;

        if (box is IWiredConfiguredItem configured) {
            proposed = PreserveAdvancedConfiguration(configured, proposed);

            if (configured.Descriptor.CanonicalName == "wf_act_bot_clothes") {
                proposed = proposed with { Text = ValidateBotFigure(proposed.Text, session) };
            }

            return WiredConfigurationSave.TrySave(configured, proposed, store, out error,
                id => room.GetRoomItemHandler().GetItem(id) != null,
                publish: wired.PublishConfigured, prepare: WiredRoomOperations.PrepareSnapshots,
                isTemporaryInRoom: id => room.GetRoomItemHandler().GetItem(id)?.IsTemporary == true);
        }

        if (WiredLegacyCustomEditor.IsCustom(box)) {
            if (!WiredLegacyCustomEditor.TryPrepare(box, proposed, WiredLegacyCustomEditor.CreateCandidate,
                out var candidate, out error)) {
                return false;
            }

            if (wired.PublishLegacy(box, candidate!, () => wired.SaveBox(candidate!))) {
                return true;
            }

            error = "This Wired box is no longer attached to the room.";

            return false;
        }

        if (WiredLegacyEditorProjection.TryGetDescriptor(box, out var descriptor)) {
            if (descriptor.CanonicalName == "wf_act_bot_clothes") {
                proposed = proposed with { Text = ValidateBotFigure(proposed.Text, session) };
            }

            var candidate = wired.CreateConfiguredBox(selectedItem, descriptor);

            if (candidate == null) {
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
                if (candidate.Type == WiredBoxType.EffectBotChangesClothesBox) {
                    candidate.StringData = ValidateBotFigure(candidate.StringData, session);
                }

                return wired.PublishLegacy(original, candidate, () => wired.SaveBox(candidate));
            }, out error, id => room.GetRoomItemHandler().GetItem(id) is { IsTemporary: false });
    }

    private string ValidateBotFigure(string data, GameClient session)
    {
        var fields = data.Split('\t', 2);

        if (fields.Length == 2) {
            fields[1] = fields[1].TrimEnd('.');
        }

        if (fields.Length != 2 || !WiredBotActions.FigureWellFormed(fields[1])) {
            throw new ArgumentException("Invalid bot figure.");
        }

        var habbo = session.GetHabbo();

        // Paid clothing is only checked against a loaded wardrobe, so the figure is rejected without one.
        if (habbo.Clothing is not { } wardrobe) {
            throw new ArgumentException("Bot figure cannot be validated without a loaded wardrobe.");
        }

        var validated = figures.ProcessFigure(fields[1], habbo.Gender, wardrobe.GetClothingParts, ClubAccess.LevelFor(habbo.Access));

        return fields[0] + "\t" + validated.TrimEnd('.');
    }

    private static WiredConfiguration PreserveAdvancedConfiguration(IWiredConfiguredItem box, WiredConfiguration proposed)
    {
        var saved = box.Configuration;
        proposed = proposed with { ScoreQuotaPerGame = saved.ScoreQuotaPerGame };

        if (box.Descriptor.CanonicalName == "wf_act_place_furni" && proposed.IntParams.Length == 6 && saved.TemporaryPlacement != null) {
            proposed = proposed with
            {
                TemporaryPlacement = saved.TemporaryPlacement,
                Snapshots = saved.Snapshots,
                SecondarySelectedItems = saved.SecondarySelectedItems,
                FurniSources = saved.FurniSources,
                UserSources = saved.UserSources,
                VariableIds = saved.VariableIds
            };
        }

        return proposed;
    }
}
