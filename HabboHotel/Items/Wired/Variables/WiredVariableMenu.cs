using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Request-scoped menu queries; callers must check room editing rights before constructing a response.</summary>
public sealed class WiredVariableMenu(Room room, WiredRoomVariables variables)
{
    public WiredVariableCatalog Catalog() => variables.Catalog();

    public WiredVariableMenuSnapshot Snapshot()
    {
        var catalog = Catalog(); var (frame, _) = LiveHolders(WiredVariableTarget.Global);
        var references = catalog.Variables.Where(x => x.Definition.Target != WiredVariableTarget.Context)
            .Select(x => new WiredVariableReference(x.Definition.Target, x.Definition.Token)).ToArray();
        using var reads = variables.Module.CaptureReads(references, frame);
        var assignments = new List<WiredVariableStoredHolder>();
        foreach (var variable in catalog.Variables.Where(x => x.Definition.Target != WiredVariableTarget.Context))
        {
            var reference = new WiredVariableReference(variable.Definition.Target, variable.Definition.Token);
            var holders = variable.Definition.Target == WiredVariableTarget.Global ? [new WiredVariableHolder(WiredVariableTarget.Global, 0, 0)]
                : frame.Holders.Where(x => x.Target == variable.Definition.Target).ToArray();
            foreach (var holder in holders)
                if (reads.Read(reference, holder, frame) is { } value)
                    assignments.Add(new(new(variable.Definition.ItemId, holder.Target, holder.StableId), "", value));
        }
        return new(room.Id, catalog.Variables, ToWireHolders(assignments));
    }

    public WiredVariableHolderPage Page(WiredVariableDescription variable, int page, int size, int userFilter, int sort)
    {
        var (frame, names) = LiveHolders(variable.Definition.Target);
        var result = variable.IsBuiltin ? WiredVariablePaging.Page(ReadLive(variable, frame, names), page, size, sort)
            : variables.Module.ReadHolderPage(variable.Definition.ItemId, page, size, sort,
            variable.Definition.Target == WiredVariableTarget.User && userFilter == 1
                ? frame.Holders.Where(x => x.Target == WiredVariableTarget.User).Select(x => x.StableId).ToArray() : null, names);
        return result with { Holders = ToWireHolders(result.Holders) };
    }

    // The unpaged overview only highlights live room entities. Offline holders use bounded pages.
    public IReadOnlyList<WiredVariableStoredHolder> Live(WiredVariableDescription variable)
    {
        var (frame, names) = LiveHolders(variable.Definition.Target);
        return ToWireHolders(ReadLive(variable, frame, names));
    }
    private IReadOnlyList<WiredVariableStoredHolder> ReadLive(WiredVariableDescription variable, WiredVariableFrame frame,
        IReadOnlyDictionary<long, string> names)
    {
        if (variable.Definition.Target == WiredVariableTarget.Context) return [];
        var reference = new WiredVariableReference(variable.Definition.Target, variable.Definition.Token);
        var holders = variable.Definition.Target == WiredVariableTarget.Global
            ? new[] { new WiredVariableHolder(WiredVariableTarget.Global, 0, 0) }
            : frame.Holders.Where(x => x.Target == variable.Definition.Target).ToArray();
        using var reads = variables.Module.CaptureReads([reference], frame);
        var result = new List<WiredVariableStoredHolder>();
        foreach (var holder in holders)
            if (reads.Read(reference, holder, frame) is { } value)
                result.Add(new(new(variable.Definition.ItemId, holder.Target, holder.StableId), names.GetValueOrDefault(holder.StableId) ?? "", value));
        return result;
    }
    private IReadOnlyList<WiredVariableStoredHolder> ToWireHolders(IReadOnlyList<WiredVariableStoredHolder> holders)
    {
        var bots = room.GetRoomUserManager().GetRoomUsers().Where(x => x.IsBot)
            .ToDictionary(x => WiredVariableRuntimeFrames.UserHolder(x).StableId, MenuEntityId);
        return holders.Where(x => x.Key.Target != WiredVariableTarget.User || x.Key.HolderId > 0 || bots.GetValueOrDefault(x.Key.HolderId) != 0)
            .Select(x => x.Key.Target == WiredVariableTarget.Global
                ? x with { Key = x.Key with { HolderId = room.Id }, Name = room.Name }
                : x.Key.Target == WiredVariableTarget.User && x.Key.HolderId < 0 ? x with { Key = x.Key with { HolderId = bots[x.Key.HolderId] } } : x).ToArray();
    }
    public static int MenuEntityId(RoomUser user)
    {
        if (!user.IsBot) return user.HabboId;
        var id = user.IsPet ? user.PetData?.PetId ?? 0 : user.BotData.Id;
        return id is > 0 and <= 1073741823 ? -(id * 2 + (user.IsPet ? 0 : 1)) : 0;
    }
    private (WiredVariableFrame Frame, IReadOnlyDictionary<long, string> Names) LiveHolders(WiredVariableTarget target)
    {
        var users = room.GetRoomUserManager().GetRoomUsers().ToArray();
        var items = room.GetRoomItemHandler().GetWallAndFloor.ToArray();
        var holders = users.Select(WiredVariableRuntimeFrames.UserHolder).Concat(items.Select(item =>
            new WiredVariableHolder(WiredVariableTarget.Furni, item.Id, checked((int)item.Id), item.OwnerId > 0))).ToArray();
        var names = new Dictionary<long, string>();
        if (target == WiredVariableTarget.User)
            foreach (var user in users) names[WiredVariableRuntimeFrames.UserHolder(user).StableId] = user.IsBot ? user.BotData.Name : user.GetUsername();
        else if (target == WiredVariableTarget.Furni)
            foreach (var item in items) names[item.Id] = item.Definition.PublicName;
        else names[0] = room.Name;
        return (new(room.Id, holders), names);
    }
}
