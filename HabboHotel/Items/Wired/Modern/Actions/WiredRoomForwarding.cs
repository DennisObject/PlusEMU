using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

public static class WiredRoomForwarding
{
    public sealed record Destination(uint RoomId, uint TeleporterId = 0);
    public static WiredConfiguration Defaults() => new() { IntParams = [0, 100] };
    public static bool TryValidate(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed; error = "Invalid room forwarding configuration.";
        if (!WiredLegacyProtocol.IsWithinLimits(proposed) || proposed.IntParams.Length != 2
            || proposed.IntParams[0] is not (0 or 10 or 11 or 200 or 201)
            || proposed.IntParams[1] is not (0 or 100 or 200 or 201)
            || proposed.Text.Length > 0 && !PositiveId(proposed.Text, out _)) return false;
        validated = proposed with
        {
            UserSources = ImmutableDictionary<string, int>.Empty.Add("users", proposed.IntParams[0]),
            FurniSources = ImmutableDictionary<string, int>.Empty.Add("links", proposed.IntParams[1])
        };
        error = ""; return true;
    }

    public static Destination? Resolve(IEnumerable<Item> items, string roomText, Func<uint, uint> roomOfItem, Func<uint, uint> pairedItem)
    {
        foreach (var item in items)
        {
            if (item.ExtraData is MapDataFormat map && map.Data.TryGetValue("internalLink", out var link) && PositiveId(link, out var linkedRoom))
                return new(linkedRoom);
            var raw = item.ExtraData.Serialize();
            if (raw.TrimStart().StartsWith('{'))
            {
                try
                {
                    using var document = JsonDocument.Parse(raw);
                    if (document.RootElement.TryGetProperty("room_linker", out var section) && section.ValueKind == JsonValueKind.Object)
                    {
                        if (section.TryGetProperty("RoomId", out var room) && room.ValueKind == JsonValueKind.Number && room.TryGetInt32(out var roomId) && roomId > 0) return new((uint)roomId);
                        if (section.TryGetProperty("ItemId", out var pair) && pair.ValueKind == JsonValueKind.Number && pair.TryGetInt32(out var pairId) && pairId > 0)
                        {
                            var destination = roomOfItem((uint)pairId);
                            if (destination is > 0 and <= int.MaxValue) return new(destination, (uint)pairId);
                        }
                    }
                }
                catch (JsonException error) { NLog.LogManager.GetLogger("Wired").Warn(error, "Invalid room_linker data on item {0}", item.Id); }
            }
            if (item.Definition.InteractionType != InteractionType.Teleport || item.Id > int.MaxValue) continue;
            var paired = pairedItem(item.Id);
            if (paired == 0 || paired > int.MaxValue) continue;
            var destinationRoom = roomOfItem(paired);
            if (destinationRoom is > 0 and <= int.MaxValue) return new(destinationRoom, paired);
        }
        return PositiveId(roomText, out var fallback) ? new(fallback) : null;
    }

    public static bool Execute(WiredRuntimeContext context, WiredConfiguration config)
    {
        var items = context.Targets.ResolveFurni(context, config.SelectedItems, config.FurniSources["links"]);
        var target = Resolve(items, config.Text, id => ItemTeleporterFinder.GetTeleRoomId(id, context.Room), ItemTeleporterFinder.GetLinkedTele);
        if (target == null || target.RoomId == context.Room.RoomId) return false;
        var forwarded = false;
        foreach (var user in context.Targets.ResolveUsers(context, [], config.UserSources["users"]).Where(user => !user.IsBot))
        {
            var client = user.GetClient(); var habbo = client?.GetHabbo();
            if (client == null || habbo == null || !ReferenceEquals(habbo.CurrentRoom, context.Room)) continue;
            habbo.IsTeleporting = target.TeleporterId != 0;
            habbo.TeleportingRoomId = target.TeleporterId == 0 ? 0 : target.RoomId;
            habbo.TeleporterId = target.TeleporterId;
            client.Send(new RoomForwardComposer(target.RoomId));
            context.Room.GetWired().RecordRoomNetworkForward(user, target.RoomId);
            forwarded = true;
        }
        return forwarded;
    }

    private static bool PositiveId(string text, out uint id) => uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id is > 0 and <= int.MaxValue;
}
