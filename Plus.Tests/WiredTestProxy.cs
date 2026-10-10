using System.Reflection;

namespace Plus.Tests;

internal static class WiredTestProxy
{
    public class UnusedFigure : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected figure lookup: {method?.Name}");
    }

    public class Proxy : DispatchProxy
    {
        private Func<MethodInfo, object?[]?, object?> _invoke = null!;
        public static T Create<T>(Func<MethodInfo, object?[]?, object?> invoke) where T : class
        {
            var value = Create<T, Proxy>();
            ((Proxy)(object)value)._invoke = invoke;

            return value;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => _invoke(method!, args);
    }
}
