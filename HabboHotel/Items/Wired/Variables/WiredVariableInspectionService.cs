using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

public interface IWiredVariableInspectionService
{
    void Read(Room room, GameClient session, WiredVariableInspectionRequest request);
}

public sealed record WiredVariableInspectionRequest(int RequestId, uint RoomId, int Target, int EntityId, int Domain);
public enum WiredVariableInspectionStatus
{
    Success, Refused, Unavailable, Unsupported
}
public sealed record WiredVariableInspectionEntry(string Token, long Value);

public sealed class WiredVariableInspectionService(ILogger<WiredVariableInspectionService> logger) : IWiredVariableInspectionService
{
    internal static IReadOnlyList<string> Tokens => WiredWallBuiltinValues.Tokens;

    public void Read(Room room, GameClient session, WiredVariableInspectionRequest request)
    {
        var status = WiredVariableInspectionStatus.Unavailable;
        WiredVariableInspectionEntry[] entries = [];

        try {
            (status, entries) = Capture();
        }
        catch (Exception error) {
            logger.LogWarning(error, "Could not capture wall variable inspection in room {RoomId} for item {ItemId}", room.Id, request.EntityId);
        }

        // A final permission check can only downgrade the captured result, never recover a failed read.
        try {
            if (!CanRead()) {
                status = WiredVariableInspectionStatus.Refused;
                entries = [];
            }
        }
        catch (Exception error) {
            logger.LogWarning(error, "Could not authorize wall variable inspection in room {RoomId} for item {ItemId}", room.Id, request.EntityId);
            status = WiredVariableInspectionStatus.Unavailable;
            entries = [];
        }

        session.Send(new WiredVariableInspectionDataComposer(request, status, entries));

        (WiredVariableInspectionStatus Status, WiredVariableInspectionEntry[] Entries) Capture()
        {
            if (!CanRead()) {
                return (WiredVariableInspectionStatus.Refused, []);
            }

            if (request.Target != 1 || request.Domain != 1) {
                return (WiredVariableInspectionStatus.Unsupported, []);
            }

            var original = room.GetWired().CaptureVariableInspectionFrame();
            var context = original.RuntimeContext!;

            if (!context.FurniIdentity.TryGetValue((uint)request.EntityId, out var item) || !item.IsWallItem) {
                return (WiredVariableInspectionStatus.Unavailable, []);
            }

            WiredWallInspectionSnapshot? captured = null;

            lock (item.NavSync) {
                var map = room.GetGameMap();
                var model = map.StaticModel;
                var holder = WiredVariableRuntimeFrames.FurniHolder(item);
                var definition = item.Definition;
                var raw = item.WallCoordinates;

                if (item.RoomId == room.Id && item.IsWallItem && item.Id == (uint)request.EntityId
                    && ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item)
                    && model is not null && WiredWallSnapshot.TryParse(raw, out var position)) {
                    captured = new(room, map, model, item, definition, holder, context, raw,
                        new(item.Id, definition.SpriteId, position!, WiredWallGeometry.CaptureAltitudeInputs(model, position!)));
                }
            }

            if (captured is null) {
                return (WiredVariableInspectionStatus.Unavailable, []);
            }

            var selected = new WiredVariableFrame(room.Id, [captured.Holder]) { RuntimeContext = context, WallInspectionSnapshot = captured };
            var references = Tokens.Select(token => new WiredVariableReference(WiredVariableTarget.Furni, "internal:" + token)).ToArray();
            WiredVariableValue?[] values;

            using (var reads = room.GetWired().Variables.Module.CaptureReads(references, selected)) {
                values = references.Select(reference => reads.Read(reference, captured.Holder, selected)).ToArray();
            }

            if (values.Any(value => value is null)) {
                return (WiredVariableInspectionStatus.Unavailable, []);
            }

            bool current;

            lock (item.NavSync) {
                current = captured.Matches(room, item, captured.Holder, selected);
            }

            return current
                ? (WiredVariableInspectionStatus.Success, Tokens.Select((token, index) => new WiredVariableInspectionEntry(token, values[index]!.Value)).ToArray())
                : (WiredVariableInspectionStatus.Unavailable, []);
        }

        bool CanRead() => ReferenceEquals(session.GetHabbo().CurrentRoom, room) && room.Id == request.RoomId
            && room.GetWired().Settings.CanInspect(session);
    }
}
