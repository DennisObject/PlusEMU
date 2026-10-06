using System.Reflection;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.Chat.Commands;

namespace Plus.Tests;

internal sealed class TestWiredCommands : ICommandManager
{
    public static TestWiredCommands Unused { get; } = new();

    public Task<bool> Parse(GameClient session, string message) =>
        throw new InvalidOperationException("Unexpected command parse.");
    public void Register(string commandText, ICommandBase command) =>
        throw new InvalidOperationException("Unexpected command registration.");
    public void LogCommand(int userId, string data, string machineId) =>
        throw new InvalidOperationException("Unexpected command logging.");
    public bool TryGetCommand(string command, out ICommandBase? chatCommand) =>
        throw new InvalidOperationException("Unexpected command lookup.");
}

internal static class TestWiredAccess
{
    public static IAccessControl Unused
    {
        get;
    } = Create((method, _) =>
        throw new InvalidOperationException("Unexpected access lookup: " + method.Name));

    public static IAccessControl Create(Func<MethodInfo, object?[]?, object?> handler)
    {
        var access = DispatchProxy.Create<IAccessControl, Proxy>();
        ((Proxy)(object)access).Handler = handler;

        return access;
    }

    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
