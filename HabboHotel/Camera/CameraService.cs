using System.Runtime.CompilerServices;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plus.Communication.Packets.Outgoing.Camera;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Camera;

[Singleton]
public interface ICameraService
{
    Task Handle(GameClient session, IIncomingPacket packet, bool thumbnail);
    CameraCheckoutResult Checkout(GameClient session, Guid mediaId, Func<CameraCheckoutMedia, CameraCheckoutResult> operation);
}

public sealed class CameraService : ICameraService, IDisposable
{
    private readonly ConditionalWeakTable<GameClient, State> _sessions = new();
    private readonly CameraConfiguration _options;
    private readonly ISettingsManager _settings;
    private readonly IDatabase _database;
    private readonly ICameraEffectCatalogue _catalogue;
    private readonly TimeProvider _time;
    private readonly ILogger<CameraService> _logger;
    private readonly CameraRendererClient _renderer;
    private readonly CameraQuota _quota;
    private readonly string _directory;
    private DateTimeOffset _lastCleanup;
    private readonly Lock _cleanupGate = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public CameraService(IOptions<CameraConfiguration> options, ISettingsManager settings, IDatabase database,
        ICameraEffectCatalogue catalogue, TimeProvider time, ILogger<CameraService> logger)
    {
        _options = options.Value; _settings = settings; _database = database; _catalogue = catalogue; _time = time; _logger = logger;
        _renderer = new(options); _quota = new(database, settings, time);
        _directory = Path.GetFullPath(_options.OutputDirectory);
        Directory.CreateDirectory(_directory);
    }

    private State Session(GameClient client) => _sessions.GetValue(client, session =>
    {
        var state = new State();
        session.CameraContextEnded += () => { lock (state.Gate) { state.Cancellation.Cancel(); state.Drafts.Clear(); state.Requests.Clear(); } };
        return state;
    });
    private bool Allowed(GameClient session, Room room, bool thumbnail)
    {
        if (!session.IsAuthenticated || !CameraAccess.HasPermission(_settings, session.GetHabbo()) || session.GetHabbo().WalletClosed ||
            session.GetHabbo().CurrentRoom != room || room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id) == null) return false;
        return !thumbnail || room.CheckRights(session, true, true);
    }
    private void Expire(State state)
    {
        var now = _time.GetUtcNow();
        foreach (var key in state.Drafts.Where(pair => now - pair.Value.CreatedAt >= Lifetime).Select(pair => pair.Key).ToArray()) state.Drafts.Remove(key);
    }
    public CameraCheckoutResult Checkout(GameClient session, Guid mediaId, Func<CameraCheckoutMedia, CameraCheckoutResult> operation)
    {
        var habbo = session.GetHabbo();
        if (habbo == null || !_sessions.TryGetValue(session, out var state)) return new(false, "unavailable");
        // Match disconnect's wallet-before-context lock order. Authorization and payment
        // finish together, so deletion or leaving the room cannot invalidate a checked GUID midway.
        lock (habbo.WalletSync)
        lock (state.Gate)
        {
            Expire(state);
            if (state.Cancellation.IsCancellationRequested || habbo.CurrentRoom is not { } room || !Allowed(session, room, false))
                return new(false, "unavailable");
            foreach (var draft in state.Drafts.Values.Where(draft => draft.RoomId == room.RoomId))
                if (draft.Media.TryGetValue(mediaId, out var renderedAt)) return operation(new(mediaId, room.RoomId, renderedAt));
            return new(false, "unavailable");
        }
    }
    public async Task Handle(GameClient session, IIncomingPacket packet, bool thumbnail)
    {
        CameraParseResult? parsed = null;
        bool quotaHit = false;
        try
        {
            var configured = _options.Effects.ToDictionary(effect => effect.Name, effect => effect.MinLevel, StringComparer.Ordinal);
            var level = session.GetHabbo()?.GetAchievementData("ACH_CameraPhotoCount")?.Level ?? 0;
            parsed = CameraRequestParser.Parse(packet, thumbnail ? CameraChannel.Thumbnail : CameraChannel.Photo, configured, level);
            if (parsed.Status != CameraParseStatus.Accepted || parsed.Command == null) { Failure(session, parsed, thumbnail, false); return; }
            var state = Session(session);
            var room = session.GetHabbo()?.CurrentRoom;
            if (room == null || !Allowed(session, room, thumbnail)) { Failure(session, parsed, thumbnail, false); return; }
            Draft? draft = null;
            CameraViewport viewport;
            IReadOnlyList<CameraEffectSelection> effects = [];
            bool zoom = false;
            bool capturing = parsed.Command is CameraCommand.Capture;
            CancellationTokenSource generation;
            lock (state.Gate)
            {
                Expire(state);
                if (state.Cancellation.IsCancellationRequested) state.Cancellation = new();
                generation = state.Cancellation;
                if (parsed.Command is CameraCommand.Delete delete)
                {
                    state.Drafts.Remove(Guid.Parse(delete.DraftId));
                    return;
                }
                if (state.RequestDay != _time.GetUtcNow().Date) { state.Requests.Clear(); state.RequestDay = _time.GetUtcNow().Date; }
                if (state.Requests.Count >= 512) { Failure(session, parsed, thumbnail, true); return; }
                if (!state.Requests.Add(Guid.Parse(parsed.RequestId))) { Failure(session, parsed, thumbnail, false); return; }
                if (parsed.Command is CameraCommand.Capture capture)
                {
                    if (!thumbnail && state.Drafts.Count >= 5) { Failure(session, parsed, false, false); return; }
                    viewport = capture.Viewport;
                }
                else if (parsed.Command is CameraCommand.Render render)
                {
                    if (!state.Drafts.TryGetValue(Guid.Parse(render.DraftId), out draft) || draft.RoomId != room.RoomId)
                    { Failure(session, parsed, thumbnail, false); return; }
                    viewport = draft.Viewport; effects = render.Effects; zoom = render.Zoom;
                }
                else return;
            }
            if (effects.Count > 0)
            {
                var catalogue = _catalogue.Current().ToDictionary(effect => effect.Name, StringComparer.Ordinal);
                if (effects.Any(effect => !catalogue.TryGetValue(effect.Name, out var definition) || definition.MinLevel > level) ||
                    effects.Count(effect => catalogue[effect.Name].Type == "frame") > 1) { Failure(session, parsed, thumbnail, false); return; }
            }
            if (!_quota.Reserve(session.GetHabbo().Id, !capturing, thumbnail)) { quotaHit = true; Failure(session, parsed, thumbnail, true); return; }
            var scene = capturing ? JsonSerializer.SerializeToElement(CameraSnapshotBuilder.Capture(room), Json) : draft!.Scene;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(generation.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var image = await _renderer.Render(scene, viewport, effects, zoom, level, deadline.Token);
            lock (state.Gate)
            {
                if (!ReferenceEquals(state.Cancellation, generation) || generation.IsCancellationRequested || deadline.IsCancellationRequested || !Allowed(session, room, thumbnail))
                { Failure(session, parsed, thumbnail, false); return; }
                if (thumbnail)
                {
                    var directory = Path.Combine(_directory, "thumbnail"); Directory.CreateDirectory(directory);
                    string temp = Path.Combine(directory, Guid.NewGuid()+".tmp");
                    File.WriteAllBytes(temp, image.Png);
                    File.Move(temp, Path.Combine(directory, room.RoomId+".png"), overwrite: true);
                    session.Send(new ThumbnailStatusComposer(true));
                }
                else
                {
                    if (!capturing && (draft == null || !state.Drafts.TryGetValue(draft.Id, out var current) || !ReferenceEquals(current, draft) || _time.GetUtcNow() - draft.CreatedAt >= Lifetime))
                    { Failure(session, parsed, false, false); return; }
                    var mediaId = Guid.NewGuid(); var now = _time.GetUtcNow();
                    string path = Path.Combine(_directory, mediaId.ToString("D"));
                    try
                    {
                        File.WriteAllBytes(path+".png", image.Png); File.WriteAllBytes(path+"_small.png", image.SmallPng);
                        using var connection = _database.Connection();
                        connection.Execute("INSERT INTO camera_media (id,user_id,room_id,created_at) VALUES (@id,@userId,@roomId,@now)",
                            new { id = mediaId.ToString("D"), userId = session.GetHabbo().Id, roomId = room.RoomId, now = now.UtcDateTime });
                    }
                    catch { File.Delete(path+".png"); File.Delete(path+"_small.png"); throw; }
                    if (capturing)
                    {
                        draft = new(Guid.NewGuid(), room.RoomId, now, scene, viewport);
                        state.Drafts.Add(draft.Id, draft);
                    }
                    draft!.Media.Add(mediaId, now);
                    session.Send(new CameraStorageUrlComposer(CameraStorageReply.Success(parsed.RequestId, draft.Id, mediaId, parsed.Stage)));
                }
            }
            try { Cleanup(); } catch (Exception exception) { _logger.LogWarning(exception, "Camera media cleanup failed"); }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Trusted camera request failed for session {SessionId}: {Reason}", session.Id, exception.Message);
            Failure(session, parsed, thumbnail, quotaHit);
        }
    }
    private static void Failure(GameClient session, CameraParseResult? parsed, bool thumbnail, bool quota)
    {
        if (parsed?.Action == "delete" || parsed?.Command is CameraCommand.Delete) return;
        if (thumbnail) session.Send(new ThumbnailStatusComposer(false, quota));
        else session.Send(new CameraStorageUrlComposer(parsed?.RequestId.Length > 0 && parsed.Stage.Length > 0
            ? CameraStorageReply.Failure(parsed.RequestId, parsed.Stage) : ""));
    }
    private void Cleanup()
    {
        lock (_cleanupGate)
        {
            var now = _time.GetUtcNow();
            if (now - _lastCleanup < TimeSpan.FromMinutes(1)) return;
            _lastCleanup = now;
            using var connection = _database.Connection();
            var expired = connection.Query<string>("""
                SELECT m.id FROM camera_media m WHERE m.created_at < @before
                AND NOT EXISTS (SELECT 1 FROM camera_purchases p WHERE p.media_id=m.id)
                AND NOT EXISTS (SELECT 1 FROM camera_publications p WHERE p.media_id=m.id)
                AND NOT EXISTS (SELECT 1 FROM camera_competition_entries p WHERE p.media_id=m.id) LIMIT 20
                """, new { before = now.Subtract(Lifetime).UtcDateTime });
            foreach (var value in expired)
            {
                if (!Guid.TryParseExact(value, "D", out var id)) continue;
                if (connection.State != System.Data.ConnectionState.Open) connection.Open();
                using var transaction = connection.BeginTransaction();
                connection.QuerySingleOrDefault<string>("SELECT id FROM camera_media WHERE id=@id FOR UPDATE", new { id = value }, transaction);
                int retained = connection.QuerySingle<int>("SELECT (SELECT COUNT(*) FROM camera_purchases WHERE media_id=@id)+(SELECT COUNT(*) FROM camera_publications WHERE media_id=@id)+(SELECT COUNT(*) FROM camera_competition_entries WHERE media_id=@id)", new { id = value }, transaction);
                if (retained > 0) continue;
                connection.Execute("DELETE FROM camera_media WHERE id=@id", new { id = value }, transaction);
                transaction.Commit();
                File.Delete(Path.Combine(_directory, id+".png")); File.Delete(Path.Combine(_directory, id+"_small.png"));
            }
            connection.Execute("DELETE FROM camera_quota WHERE quota_date < @before", new { before = now.AddDays(-2).Date });
        }
    }
    private sealed class State
    {
        public Lock Gate { get; } = new();
        public CancellationTokenSource Cancellation { get; set; } = new();
        public Dictionary<Guid, Draft> Drafts { get; } = new();
        public HashSet<Guid> Requests { get; } = new();
        public DateTime RequestDay { get; set; }
    }
    private sealed record Draft(Guid Id, uint RoomId, DateTimeOffset CreatedAt, JsonElement Scene, CameraViewport Viewport)
    {
        public Dictionary<Guid, DateTimeOffset> Media { get; } = new();
    }
    public void Dispose() => _renderer.Dispose();
}
