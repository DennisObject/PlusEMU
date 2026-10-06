using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

public interface IWiredVariableMenuService
{
    void ShowCatalogHash(Room room, GameClient session);
    void ShowCatalogDiff(Room room, GameClient session, IReadOnlyDictionary<string, int> known);
    void ShowSnapshot(Room room, GameClient session);
    void ShowHolders(Room room, GameClient session, string id);
    void ShowHolderPage(Room room, GameClient session, string id, int page, int size, int users, int sort);
    void Write(Room room, GameClient session, WiredVariableMenuWrite request);
    void Manage(Room room, GameClient session, WiredVariableMenuWrite request);
}

/// <summary>Editor catalog, holder and write orchestration; owns the inspect and modify rights and every response it sends.</summary>
public sealed class WiredVariableMenuService : IWiredVariableMenuService
{
    public void ShowCatalogHash(Room room, GameClient session)
    {
        if (!room.GetWired().Settings.CanInspect(session)) return;
        session.Send(new WiredAllVariablesHashComposer(room.GetWired().Variables.Catalog().Hash));
    }

    public void ShowCatalogDiff(Room room, GameClient session, IReadOnlyDictionary<string, int> known)
    {
        if (!room.GetWired().Settings.CanInspect(session)) return;
        foreach (var diff in room.GetWired().Variables.Catalog().Diff(known)) session.Send(new WiredAllVariablesDiffComposer(diff));
    }

    public void ShowSnapshot(Room room, GameClient session)
    {
        if (!room.GetWired().Settings.CanInspect(session)) return;
        session.Send(new WiredUserVariablesDataComposer(Menu(room).Snapshot()));
    }

    public void ShowHolders(Room room, GameClient session, string id)
    {
        if (!room.GetWired().Settings.CanInspect(session)) return;
        var menu = Menu(room);
        if (menu.Catalog().Find(id) is { } variable) session.Send(new WiredVariableHoldersComposer(room.Id, variable, menu.Live(variable)));
    }

    public void ShowHolderPage(Room room, GameClient session, string id, int page, int size, int users, int sort)
    {
        if (!room.GetWired().Settings.CanInspect(session)) return;
        var menu = Menu(room);
        if (menu.Catalog().Find(id) is { } variable)
            session.Send(new WiredVariableHoldersPageComposer(id, menu.Page(variable, page, size, users, sort), users, sort));
    }

    public void Write(Room room, GameClient session, WiredVariableMenuWrite request)
    {
        if (!room.GetWired().Settings.CanModify(session)) return;
        var menu = Menu(room);
        menu.Write(request.Target, request.TargetId, request.DefinitionId, request.Value, WiredVariableMutation.Set, request.Token);
        session.Send(new WiredUserVariablesDataComposer(menu.Snapshot()));
    }

    // This reaches offline holders, so clearing needs manage rights on top of ordinary modify rights.
    public void Manage(Room room, GameClient session, WiredVariableMenuWrite request)
    {
        var settings = room.GetWired().Settings;
        if (!settings.CanModify(session)) return;
        var menu = Menu(room);
        if (request.Action == 2)
        {
            if (settings.CanManage(session)) menu.Clear(request.Target, request.DefinitionId);
        }
        else menu.Write(request.Target, request.TargetId, request.DefinitionId, request.Value,
            request.Action == 1 ? WiredVariableMutation.Remove : WiredVariableMutation.Replace);
        session.Send(new WiredUserVariablesDataComposer(menu.Snapshot()));
    }

    private static WiredVariableMenu Menu(Room room) => new(room, room.GetWired().Variables);
}
