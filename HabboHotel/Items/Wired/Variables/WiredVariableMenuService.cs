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
        if (!room.GetWired().Settings.CanInspect(session)) {
            return;
        }

        session.Send(new WiredAllVariablesHashComposer(room.GetWired().Variables.Catalog().Hash));
    }

    public void ShowCatalogDiff(Room room, GameClient session, IReadOnlyDictionary<string, int> known)
    {
        if (!room.GetWired().Settings.CanInspect(session)) {
            return;
        }

        foreach (var diff in room.GetWired().Variables.Catalog().Diff(known)) {
            session.Send(new WiredAllVariablesDiffComposer(diff));
        }
    }

    public void ShowSnapshot(Room room, GameClient session)
    {
        if (!room.GetWired().Settings.CanInspect(session)) {
            return;
        }

        SendSnapshot(session, Menu(room).Snapshot());
    }

    public void ShowHolders(Room room, GameClient session, string id)
    {
        if (!room.GetWired().Settings.CanInspect(session)) {
            return;
        }

        var menu = Menu(room);

        if (menu.Catalog().Find(id) is { } variable) {
            var holders = menu.Live(variable).ToArray();

            if (WiredVariableWireProtocol.CanSend(session, holders)) {
                session.Send(new WiredVariableHoldersComposer(room.Id, variable, holders, WiredVariableWireProtocol.IsExact(session)));
            }
        }
    }

    public void ShowHolderPage(Room room, GameClient session, string id, int page, int size, int users, int sort)
    {
        if (!room.GetWired().Settings.CanInspect(session)) {
            return;
        }

        var menu = Menu(room);

        if (menu.Catalog().Find(id) is { } variable) {
            var result = menu.Page(variable, page, size, users, sort);
            result = result with { Holders = result.Holders.ToArray() };

            if (WiredVariableWireProtocol.CanSend(session, result.Holders)) {
                session.Send(new WiredVariableHoldersPageComposer(id, result, users, sort, WiredVariableWireProtocol.IsExact(session)));
            }
        }
    }

    public void Write(Room room, GameClient session, WiredVariableMenuWrite request)
    {
        if (!room.GetWired().Settings.CanModify(session)) {
            session.SendNotification("You do not have permission to change variables.");
            ShowSnapshot(room, session);

            return;
        }

        var menu = Menu(room);

        if (!menu.Write(request.Target, request.TargetId, request.DefinitionId, request.Value, WiredVariableMutation.Set, request.Token)) {
            session.SendNotification("The variable value could not be changed.");
        }

        SendSnapshot(session, menu.Snapshot());
    }

    // This reaches offline holders, so clearing needs manage rights on top of ordinary modify rights.
    public void Manage(Room room, GameClient session, WiredVariableMenuWrite request)
    {
        var settings = room.GetWired().Settings;

        if (!settings.CanModify(session)) {
            session.SendNotification("You do not have permission to change variables.");
            ShowSnapshot(room, session);

            return;
        }

        var menu = Menu(room);

        if (request.Action == 2) {
            if (settings.CanManage(session)) {
                menu.Clear(request.Target, request.DefinitionId);
            }
            else {
                session.SendNotification("You do not have permission to clear variable holders.");
            }
        }
        else {
            if (!menu.Write(request.Target, request.TargetId, request.DefinitionId, request.Value,
                request.Action == 1 ? WiredVariableMutation.Remove : WiredVariableMutation.Replace)) {
                session.SendNotification("The variable value could not be changed.");
            }
        }

        SendSnapshot(session, menu.Snapshot());
    }

    private static void SendSnapshot(GameClient session, WiredVariableMenuSnapshot snapshot)
    {
        snapshot = snapshot with { Assignments = snapshot.Assignments.ToArray() };

        if (WiredVariableWireProtocol.CanSend(session, snapshot.Assignments)) {
            session.Send(new WiredUserVariablesDataComposer(snapshot, WiredVariableWireProtocol.IsExact(session)));
        }
    }

    private static WiredVariableMenu Menu(Room room) => new(room, room.GetWired().Variables);
}
