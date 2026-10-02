using Dapper;
using Plus.Core.Settings;
using Plus.Database;

namespace Plus.HabboHotel.Camera;

internal sealed class CameraQuota(IDatabase database, ISettingsManager settings, TimeProvider time)
{
    public bool Reserve(int userId, bool edit, bool thumbnail)
    {
        int Read(string key) => int.TryParse(settings.TryGetValue(key), out int value) && value >= 0 && value <= 100_000 ? value : 0;
        int daily = Read(edit ? "camera.render.edit.daily" : "camera.render.daily");
        int cooldown = Read(edit ? "camera.render.edit.cooldown" : "camera.render.cooldown");
        var now = time.GetUtcNow().UtcDateTime;
        var args = new { userId, day = now.Date, now };
        using var connection = database.Connection(); connection.Open();
        using var transaction = connection.BeginTransaction();
        // The account lock keeps cooldowns and midnight rollover atomic across sessions.
        connection.QuerySingle<int>("SELECT id FROM users WHERE id=@userId FOR UPDATE", args, transaction);
        connection.Execute("INSERT IGNORE INTO camera_quota (user_id,quota_date) VALUES (@userId,@day)", args, transaction);
        const string columns = "captures AS Captures, edits AS Edits, TIMESTAMPDIFF(MICROSECOND,'1970-01-01',last_capture_at) AS LastCaptureAt, TIMESTAMPDIFF(MICROSECOND,'1970-01-01',last_edit_at) AS LastEditAt, TIMESTAMPDIFF(MICROSECOND,'1970-01-01',last_thumbnail_at) AS LastThumbnailAt";
        var today = connection.QuerySingle<QuotaRow>($"SELECT {columns} FROM camera_quota WHERE user_id=@userId AND quota_date=@day FOR UPDATE", args, transaction);
        var latest = connection.QuerySingleOrDefault<QuotaRow>($"SELECT {columns} FROM camera_quota WHERE user_id=@userId ORDER BY quota_date DESC LIMIT 1 OFFSET 1", args, transaction);
        long nowMicros = time.GetUtcNow().ToUnixTimeMilliseconds() * 1000;
        int count = edit ? today.Edits : today.Captures;
        var last = edit ? today.LastEditAt ?? latest?.LastEditAt : today.LastCaptureAt ?? latest?.LastCaptureAt;
        var lastThumbnail = today.LastThumbnailAt ?? latest?.LastThumbnailAt;
        if (count >= daily || (last.HasValue && (nowMicros - last.Value) < cooldown * 1_000_000L) ||
            (thumbnail && lastThumbnail.HasValue && (nowMicros - lastThumbnail.Value) < Read("camera.thumbnail.cooldown") * 1_000_000L)) return false;
        string update = edit
            ? "UPDATE camera_quota SET edits=edits+1,last_edit_at=@now WHERE user_id=@userId AND quota_date=@day"
            : thumbnail ? "UPDATE camera_quota SET captures=captures+1,last_capture_at=@now,last_thumbnail_at=@now WHERE user_id=@userId AND quota_date=@day"
            : "UPDATE camera_quota SET captures=captures+1,last_capture_at=@now WHERE user_id=@userId AND quota_date=@day";
        connection.Execute(update, args, transaction);
        transaction.Commit();
        return true;
    }
    private sealed class QuotaRow
    {
        public int Captures { get; set; }
        public int Edits { get; set; }
        public long? LastCaptureAt { get; set; }
        public long? LastEditAt { get; set; }
        public long? LastThumbnailAt { get; set; }
    }
}
