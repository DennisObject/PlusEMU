using System.Collections.Immutable;
using Plus.Communication.Packets;
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
    void ShowExactSnapshot(Room room, GameClient session);
    void ShowExactHolders(Room room, GameClient session, string id);
    void ShowExactHolderPage(Room room, GameClient session, string id, int page, int size, int users, int sort);
    void WriteExact(Room room, GameClient session, WiredVariableMenuWrite request);
    void ManageExact(Room room, GameClient session, WiredVariableMenuWrite request);
}

/// <summary>Editor catalog, holder and write orchestration; owns the inspect and modify rights and every response it sends.</summary>
public sealed class WiredVariableMenuService : IWiredVariableMenuService
{
    public void ShowCatalogHash(Room room, GameClient session)
    {
        try {
            if (!CanSendCatalog(room, session) || !room.GetWired().Variables.TryCaptureNativeCatalog(out var catalog)) {
                return;
            }

            if (CanSendCatalog(room, session)) {
                session.Send(new WiredAllVariablesHashComposer(catalog!.Hash));
            }
        }
        catch (Exception) { }
    }

    public void ShowCatalogDiff(Room room, GameClient session, IReadOnlyDictionary<string, int> known)
    {
        try {
            if (!CanSendCatalog(room, session) || !room.GetWired().Variables.TryCaptureNativeCatalog(out var catalog)) {
                return;
            }

            foreach (var diff in catalog!.Diff(known)) {
                if (!CanSendCatalog(room, session)) {
                    return;
                }

                session.Send(new WiredNativeCatalogDiffComposer(diff));
            }
        }
        catch (Exception) { }
    }

    // Permission precedes current-room proof on each send; neither is held across transport callbacks.
    private static bool CanSendCatalog(Room room, GameClient session) => room.GetWired().Settings.CanInspect(session)
        && ReferenceEquals(session.GetHabbo().CurrentRoom, room);

    public void ShowSnapshot(Room room, GameClient session) => ShowSnapshot(room, session, false);
    public void ShowExactSnapshot(Room room, GameClient session) => ShowSnapshot(room, session, true);
    public void ShowHolders(Room room, GameClient session, string id) => ShowHolders(room, session, id, false);
    public void ShowExactHolders(Room room, GameClient session, string id) => ShowHolders(room, session, id, true);
    public void ShowHolderPage(Room room, GameClient session, string id, int page, int size, int users, int sort) =>
        ShowHolderPage(room, session, id, page, size, users, sort, false);
    public void ShowExactHolderPage(Room room, GameClient session, string id, int page, int size, int users, int sort) =>
        ShowHolderPage(room, session, id, page, size, users, sort, true);
    public void Write(Room room, GameClient session, WiredVariableMenuWrite request) => Write(room, session, request, false);
    public void WriteExact(Room room, GameClient session, WiredVariableMenuWrite request) => Write(room, session, request, true);
    public void Manage(Room room, GameClient session, WiredVariableMenuWrite request) => Manage(room, session, request, false);
    public void ManageExact(Room room, GameClient session, WiredVariableMenuWrite request) => Manage(room, session, request, true);

    private static void ShowSnapshot(Room room, GameClient session, bool exact)
    {
        if (!CanInspect(room, session)) {
            return;
        }

        SendSnapshot(room, session, Menu(room).Snapshot(), exact);
    }

    private static void ShowHolders(Room room, GameClient session, string id, bool exact)
    {
        if (!CanInspect(room, session)) {
            return;
        }

        var menu = Menu(room);

        if (menu.Catalog().Find(id) is { } variable) {
            variable = WiredVariableWireCapture.Capture(variable);
            var holders = menu.Live(variable).ToImmutableArray();
            SendFrozen(room, session, holders, exact, () => new WiredVariableHoldersComposer(room.Id, variable, holders, exact));
        }
    }

    private static void ShowHolderPage(Room room, GameClient session, string id, int page, int size, int users, int sort, bool exact)
    {
        if (!CanInspect(room, session)) {
            return;
        }

        var menu = Menu(room);

        if (menu.Catalog().Find(id) is { } variable) {
            var result = menu.Page(variable, page, size, users, sort);
            result = result with { Holders = result.Holders.ToImmutableArray() };
            SendFrozen(room, session, result.Holders, exact, () => new WiredVariableHoldersPageComposer(id, result, users, sort, exact));
        }
    }

    private static void Write(Room room, GameClient session, WiredVariableMenuWrite request, bool exact)
    {
        if (!ReferenceEquals(session.GetHabbo().CurrentRoom, room) || !room.GetWired().Settings.CanModify(session)) {
            session.SendNotification("You do not have permission to change variables.");
            ShowSnapshot(room, session, exact);

            return;
        }

        var menu = Menu(room);

        if (!menu.Write(request.Target, request.TargetId, request.DefinitionId, request.Value, WiredVariableMutation.Set, request.Token)) {
            session.SendNotification("The variable value could not be changed.");
        }

        SendSnapshot(room, session, menu.Snapshot(), exact);
    }

    // Clearing can reach offline holders and needs manage rights as well as modify rights.
    private static void Manage(Room room, GameClient session, WiredVariableMenuWrite request, bool exact)
    {
        var settings = room.GetWired().Settings;

        if (!ReferenceEquals(session.GetHabbo().CurrentRoom, room) || !settings.CanModify(session)) {
            session.SendNotification("You do not have permission to change variables.");
            ShowSnapshot(room, session, exact);

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
        else if (!menu.Write(request.Target, request.TargetId, request.DefinitionId, request.Value,
            request.Action == 1 ? WiredVariableMutation.Remove : WiredVariableMutation.Replace)) {
            session.SendNotification("The variable value could not be changed.");
        }

        SendSnapshot(room, session, menu.Snapshot(), exact);
    }

    private static void SendSnapshot(Room room, GameClient session, WiredVariableMenuSnapshot snapshot, bool exact)
    {
        snapshot = snapshot with
        {
            Definitions = snapshot.Definitions.Select(WiredVariableWireCapture.Capture).ToImmutableArray(),
            Assignments = snapshot.Assignments.ToImmutableArray()
        };
        SendFrozen(room, session, snapshot.Assignments, exact, () => new WiredUserVariablesDataComposer(snapshot, exact));
    }

    // Preflight and compose the same immutable values; recheck captured room and current rights at the send boundary.
    private static void SendFrozen(Room room, GameClient session, IReadOnlyList<WiredVariableStoredHolder> values,
        bool exact, Func<IServerPacket> compose)
    {
        if (!CanInspect(room, session)) {
            return;
        }

        if (!WiredVariableWireProtocol.CanSend(values, exact)) {
            if (CanInspect(room, session)) {
                session.SendNotification("This variable contains a 64-bit value. Use the exact variable endpoint to inspect it.");
            }

            return;
        }

        var packet = compose();

        if (CanInspect(room, session)) {
            session.Send(packet);
        }
    }

    private static bool CanInspect(Room room, GameClient session) =>
        ReferenceEquals(session.GetHabbo().CurrentRoom, room) && room.GetWired().Settings.CanInspect(session);
    private static WiredVariableMenu Menu(Room room) => new(room, room.GetWired().Variables);
}
