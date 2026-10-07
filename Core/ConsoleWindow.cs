namespace Plus.Core;

/// <summary>
/// Title, cursor, clearing and key prompts only work on an interactive console.
/// As a service (systemd, a Windows service) there is none, and Windows throws on them.
/// </summary>
public static class ConsoleWindow
{
    private static bool OutputIsConsole => !Console.IsOutputRedirected;

    public static void SetTitle(string title)
    {
        if (OutputIsConsole) {
            Console.Title = title;
        }
    }

    public static void HideCursor()
    {
        if (OutputIsConsole) {
            Console.CursorVisible = false;
        }
    }

    public static void Clear()
    {
        if (OutputIsConsole) {
            Console.Clear();
        }
    }

    public static void WaitForKey()
    {
        if (!Console.IsInputRedirected) {
            Console.ReadKey(true);
        }
    }
}
