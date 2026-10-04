using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Plus.Core;

public static class ExceptionLogger
{
    private static ILogger _sqlLogger = NullLogger.Instance;
    private static ILogger _threadLogger = NullLogger.Instance;
    private static ILogger _defaultLogger = NullLogger.Instance;
    private static ILogger _criticalExceptionLogger = NullLogger.Instance;
    private static ILogger _wiredLogger = NullLogger.Instance;

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
        _sqlLogger.LogError(exception, "Error in query:\r\n{Query}", query);
    }

    public static void LogException(Exception exception)
    {
        _defaultLogger.LogError(exception, "Unhandled exception");
    }

    public static void LogCriticalException(Exception exception)
    {
        _criticalExceptionLogger.LogCritical(exception, "Critical exception");
    }

    public static void LogThreadException(Exception exception)
    {
        _threadLogger.LogError(exception, "Thread exception");
    }

    public static void LogWiredException(Exception exception)
    {
        _wiredLogger.LogError(exception, "Wired exception");
    }
}
