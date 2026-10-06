namespace Plus.Tests;

// Explicit clock double for fixtures that need a fixed instant.
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public static readonly DateTimeOffset Epoch = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    public override DateTimeOffset GetUtcNow() => now;
}
