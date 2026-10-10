using Xunit;

namespace Plus.Tests;

// These tests swap process-wide state (the static database) and lock shared rows, so they never run alongside others.
[CollectionDefinition("SharedDatabase", DisableParallelization = true)]
public sealed class SharedDatabaseCollection;

public sealed class SharedDatabaseFactAttribute : FactAttribute
{
    public SharedDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_HOUSEKEEPING_TEST_CONNECTION_STRING"))) {
            Skip = "Set PLUS_HOUSEKEEPING_TEST_CONNECTION_STRING to a disposable task_housekeeping_tests_ database with the full Plus schema.";
        }
    }
}
