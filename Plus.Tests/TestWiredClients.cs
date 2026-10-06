using System.Reflection;
using Plus.HabboHotel.GameClients;

namespace Plus.Tests;

internal static class TestWiredClients
{
    public static IGameClientManager Empty { get; } = Create(() => 0);

    public static IGameClientManager Create(Func<int> count)
    {
        var clients = DispatchProxy.Create<IGameClientManager, CountProxy>();
        ((CountProxy)(object)clients).ReadCount = count;

        return clients;
    }

    public class CountProxy : DispatchProxy
    {
        public Func<int> ReadCount { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) =>
            method!.Name == "get_Count" ? ReadCount() : throw new InvalidOperationException("Unused Wired client operation: " + method.Name);
    }
}
