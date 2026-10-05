using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

public sealed record WiredVariableHoldersView(WiredVariableDescription Variable, IReadOnlyList<WiredVariableStoredHolder> Holders);
public sealed record WiredVariableHolderPageView(WiredVariableDescription Variable, WiredVariableHolderPage Page);

public interface IWiredVariableMenuService
{
    int CatalogHash(Room room);
    IReadOnlyList<WiredVariableCatalogDiff> CatalogDiff(Room room, IReadOnlyDictionary<string, int> known);
    WiredVariableMenuSnapshot Snapshot(Room room);
    WiredVariableHoldersView? Holders(Room room, string id);
    WiredVariableHolderPageView? HolderPage(Room room, string id, int page, int size, int users, int sort);
    WiredVariableMenuSnapshot Write(Room room, WiredVariableMenuWrite request);
    WiredVariableMenuSnapshot Manage(Room room, WiredVariableMenuWrite request, bool mayClear);
}

/// <summary>Editor catalog, holder and write orchestration; callers check inspect, modify and manage rights first.</summary>
public sealed class WiredVariableMenuService : IWiredVariableMenuService
{
    public int CatalogHash(Room room) => room.GetWired().Variables.Catalog().Hash;

    public IReadOnlyList<WiredVariableCatalogDiff> CatalogDiff(Room room, IReadOnlyDictionary<string, int> known) =>
        room.GetWired().Variables.Catalog().Diff(known);

    public WiredVariableMenuSnapshot Snapshot(Room room) => Menu(room).Snapshot();

    public WiredVariableHoldersView? Holders(Room room, string id)
    {
        var menu = Menu(room);
        return menu.Catalog().Find(id) is { } variable ? new(variable, menu.Live(variable)) : null;
    }

    public WiredVariableHolderPageView? HolderPage(Room room, string id, int page, int size, int users, int sort)
    {
        var menu = Menu(room);
        return menu.Catalog().Find(id) is { } variable ? new(variable, menu.Page(variable, page, size, users, sort)) : null;
    }

    public WiredVariableMenuSnapshot Write(Room room, WiredVariableMenuWrite request)
    {
        var menu = Menu(room);
        menu.Write(request.Target, request.TargetId, request.DefinitionId, request.Value, WiredVariableMutation.Set, request.Token);
        return menu.Snapshot();
    }

    // Offline holders are reached only through the caller's manage decision; the snapshot is returned either way.
    public WiredVariableMenuSnapshot Manage(Room room, WiredVariableMenuWrite request, bool mayClear)
    {
        var menu = Menu(room);
        if (request.Action == 2)
        {
            if (mayClear) menu.Clear(request.Target, request.DefinitionId);
        }
        else menu.Write(request.Target, request.TargetId, request.DefinitionId, request.Value,
            request.Action == 1 ? WiredVariableMutation.Remove : WiredVariableMutation.Replace);
        return menu.Snapshot();
    }

    private static WiredVariableMenu Menu(Room room) => new(room, room.GetWired().Variables);
}
