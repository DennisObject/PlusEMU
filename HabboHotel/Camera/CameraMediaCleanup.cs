using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plus.Core;
using Plus.Database;

namespace Plus.HabboHotel.Camera;

// IStartable is resolved during hotel startup, even if nobody opens the camera.
public sealed class CameraMediaCleanup(IDatabase database, IOptions<CameraConfiguration> options,
    TimeProvider time, ILogger<CameraMediaCleanup> logger) : IStartable, IDisposable
{
    private const int BatchSize = 100;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _directory = Path.GetFullPath(options.Value.OutputDirectory);
    private Task? _worker;

    public Task Start()
    {
        _worker ??= Task.Run(Run);

        return Task.CompletedTask;
    }

    private async Task Run()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                try
                {
                    string cursor = "";
                    (int Count, string Cursor) batch;

                    do
                    {
                        batch = SweepBatch(cursor);
                        cursor = batch.Cursor;

                        // Drain the backlog without monopolizing a database connection.
                        if (batch.Count == BatchSize)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(100), time, _shutdown.Token);
                        }
                    } while (batch.Count == BatchSize && !_shutdown.IsCancellationRequested);

                    using var connection = database.Connection();
                    connection.Execute("DELETE FROM camera_quota WHERE quota_date < @before",
                        new
                        {
                            before = time.GetUtcNow().AddDays(-2).Date
                        });
                }
                catch (Exception exception) { logger.LogWarning(exception, "Camera media cleanup failed; retrying next sweep"); }

                await Task.Delay(TimeSpan.FromMinutes(1), time, _shutdown.Token);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    internal (int Count, string Cursor) SweepBatch(string cursor = "")
    {
        using var connection = database.Connection();
        var before = time.GetUtcNow().Subtract(CameraService.Lifetime).UtcDateTime;
        var expired = connection.Query<string>("""
            SELECT CAST(m.id AS CHAR(64)) FROM camera_media m WHERE m.id > @cursor AND m.created_at < @before
            AND NOT EXISTS (SELECT 1 FROM camera_purchases p WHERE p.media_id=m.id)
            AND NOT EXISTS (SELECT 1 FROM camera_publications p WHERE p.media_id=m.id)
            AND NOT EXISTS (SELECT 1 FROM camera_competition_entries p WHERE p.media_id=m.id)
            ORDER BY m.id LIMIT 100
            """, new
        {
            cursor,
            before
        }).ToArray();

        foreach (var value in expired)
        {
            if (_shutdown.IsCancellationRequested)
            {
                break;
            }

            if (!Guid.TryParseExact(value, "D", out var id))
            {
                continue;
            }

            try
            {
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using var transaction = connection.BeginTransaction();

                if (connection.QuerySingleOrDefault<int?>(
                    "SELECT 1 FROM camera_media WHERE id=@id AND created_at < @before FOR UPDATE",
                    new
                    {
                        id = value,
                        before
                    }, transaction) == null)
                {
                    continue;
                }

                int retained = connection.QuerySingle<int>("""
                    SELECT (SELECT COUNT(*) FROM camera_purchases WHERE media_id=@id)
                    +(SELECT COUNT(*) FROM camera_publications WHERE media_id=@id)
                    +(SELECT COUNT(*) FROM camera_competition_entries WHERE media_id=@id)
                    """, new
                {
                    id = value
                }, transaction);

                if (retained > 0)
                {
                    continue;
                }

                // Missing files are harmless. A partial unlink or failed commit keeps the
                // locked row as retryable state; checkout uses this same row lock.
                File.Delete(Path.Combine(_directory, id.ToString("D") + ".png"));
                File.Delete(Path.Combine(_directory, id.ToString("D") + "_small.png"));
                connection.Execute("DELETE FROM camera_media WHERE id=@id", new
                {
                    id = value
                }, transaction);
                transaction.Commit();
            }
            catch (Exception exception) { logger.LogWarning(exception, "Camera media {MediaId} cleanup failed; retaining retry record", value); }
        }

        return (expired.Length, expired.LastOrDefault() ?? cursor);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _worker?.GetAwaiter().GetResult();
        _shutdown.Dispose();
    }
}
