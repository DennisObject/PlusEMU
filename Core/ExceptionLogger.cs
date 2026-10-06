using Microsoft.Extensions.Logging;

namespace Plus.Core;

public static class ExceptionLogger
{
    private static ILogger? _sqlLogger;
    private static ILogger SqlLogger => _sqlLogger ?? throw new InvalidOperationException("Configure logging before use.");
    private static ILogger? _threadLogger;
    private static ILogger ThreadLogger => _threadLogger ?? throw new InvalidOperationException("Configure logging before use.");
    private static ILogger? _defaultLogger;
    private static ILogger DefaultLogger => _defaultLogger ?? throw new InvalidOperationException("Configure logging before use.");
    private static ILogger? _criticalExceptionLogger;
    private static ILogger CriticalExceptionLogger => _criticalExceptionLogger ?? throw new InvalidOperationException("Configure logging before use.");
    private static ILogger? _wiredLogger;
    private static ILogger WiredLogger => _wiredLogger ?? throw new InvalidOperationException("Configure logging before use.");

    public static void Configure(ILoggerFactory loggerFactory)
    {
        _sqlLogger = loggerFactory.CreateLogger("MySQL");
        _threadLogger = loggerFactory.CreateLogger("Thread");
        _defaultLogger = loggerFactory.CreateLogger("Exception");
        _criticalExceptionLogger = loggerFactory.CreateLogger("Critical");
        _wiredLogger = loggerFactory.CreateLogger("Wired");
    }

    public static void LogQueryError(string query, Exception exception)
    {
        SqlLogger.LogError(exception, "Error in query:\r\n{Query}", query);
    }

    public static void LogException(Exception exception)
    {
        DefaultLogger.LogError(exception, "Unhandled exception");
    }

    public static void LogCriticalException(Exception exception)
    {
        CriticalExceptionLogger.LogCritical(exception, "Critical exception");
    }

    public static void LogThreadException(Exception exception)
    {
        ThreadLogger.LogError(exception, "Thread exception");
    }

    public static void LogWiredException(Exception exception)
    {
        WiredLogger.LogError(exception, "Wired exception");
    }
}
