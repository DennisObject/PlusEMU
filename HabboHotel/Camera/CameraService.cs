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
    Task Handle(GameClient session, CameraRequestPayload payload, bool thumbnail);
    void Prepare(GameClient session);
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
    private int _preparing;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan PreparationInterval = TimeSpan.FromSeconds(30);

    public CameraService(IOptions<CameraConfiguration> options, ISettingsManager settings, IDatabase database,
        ICameraEffectCatalogue catalogue, TimeProvider time, ILogger<CameraService> logger)
    {
        _options = options.Value;
        _settings = settings;
        _database = database;
        _catalogue = catalogue;
        _time = time;
        _logger = logger;
        _renderer = new(options);
        _quota = new(database, settings, time);
        _directory = Path.GetFullPath(_options.OutputDirectory);
        Directory.CreateDirectory(_directory);
    }

    private State Session(GameClient client) => _sessions.GetValue(client, session =>
    {
        var state = new State();
        session.CameraContextEnded += () =>
        {
            lock (state.Gate) {
                state.Cancellation.Cancel();
                state.Drafts.Clear();
                state.Requests.Clear();
            }
        };

        return state;
    });
    private bool Allowed(GameClient session, Room room, bool thumbnail)
    {
        if (!session.IsAuthenticated || !CameraAccess.HasPermission(_settings, session.GetHabbo()) || session.GetHabbo().WalletClosed ||
            session.GetHabbo().CurrentRoom != room || room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id) == null) {
            return false;
        }

        return !thumbnail || room.CheckRights(session, true, true);
    }
    private void Expire(State state)
    {
        var now = _time.GetUtcNow();

        foreach (var key in state.Drafts.Where(pair => now - pair.Value.CreatedAt >= Lifetime).Select(pair => pair.Key).ToArray()) {
            state.Drafts.Remove(key);
        }
    }
    public CameraCheckoutResult Checkout(GameClient session, Guid mediaId, Func<CameraCheckoutMedia, CameraCheckoutResult> operation)
    {
        var habbo = session.GetHabbo();

        if (habbo == null || !_sessions.TryGetValue(session, out var state)) {
            return new(false, "unavailable");
        }

        // Match disconnect's wallet-before-context lock order. Authorization and payment
        // finish together, so deletion or leaving the room cannot invalidate a checked GUID midway.
        lock (habbo.WalletSync)
            lock (state.Gate) {
                Expire(state);

                if (state.Cancellation.IsCancellationRequested || habbo.CurrentRoom is not { } room || !Allowed(session, room, false)) {
                    return new(false, "unavailable");
                }

                foreach (var draft in state.Drafts.Values.Where(draft => draft.RoomId == room.RoomId)) {
                    if (draft.Media.TryGetValue(mediaId, out var renderedAt)) {
                        return operation(new(mediaId, room.RoomId, renderedAt));
                    }
                }

                return new(false, "unavailable");
            }
    }
    // Opening the camera loads the room's libraries into the renderer while the shot is framed. It reserves no quota
    // and makes no draft; the shutter still photographs the room as it is then. One preparation runs at a time, each
    // session prepares a room at most once per interval, and the packet handler never waits for it.
    public void Prepare(GameClient session)
    {
        var room = session.GetHabbo()?.CurrentRoom;

        if (room == null || !Allowed(session, room, false) || Interlocked.Exchange(ref _preparing, 1) == 1) {
            return;
        }

        var started = false;

        try {
            if (!TryBeginPreparation(session, room.RoomId, out var token)) {
                return;
            }

            var scene = JsonSerializer.SerializeToElement(CameraSnapshotBuilder.Capture(room), Json);

            _ = Task.Run(async () =>
            {
                try {
                    await _renderer.Prepare(scene, token);
                }
                catch (Exception exception) {
                    // An ended camera context cancels the preparation; it no longer matters then.
                    if (!token.IsCancellationRequested) {
                        _logger.LogWarning(exception, "Camera preparation failed for session {SessionId}: {Reason}", session.Id, exception.Message);
                    }
                }
                finally {
                    Volatile.Write(ref _preparing, 0);
                }
            });
            started = true;
        }
        catch (Exception exception) {
            _logger.LogWarning(exception, "Camera preparation failed for session {SessionId}: {Reason}", session.Id, exception.Message);
        }
        finally {
            if (!started) {
                Volatile.Write(ref _preparing, 0);
            }
        }
    }

    // False while this session prepared the room recently. Leaving a room ends the camera context; the camera opened
    // in the next room starts a new one, as a capture does.
    internal bool TryBeginPreparation(GameClient session, uint roomId, out CancellationToken token)
    {
        var state = Session(session);

        lock (state.Gate) {
            if (state.Cancellation.IsCancellationRequested) {
                state.Cancellation = new();
            }

            var now = _time.GetUtcNow();
            token = state.Cancellation.Token;

            if (state.PreparedRoomId == roomId && now - state.PreparedAt < PreparationInterval) {
                return false;
            }

            state.PreparedRoomId = roomId;
            state.PreparedAt = now;

            return true;
        }
    }

    public async Task Handle(GameClient session, CameraRequestPayload payload, bool thumbnail)
    {
        CameraParseResult? parsed = null;
        bool quotaHit = false;

        try {
            var configured = _options.Effects.ToDictionary(effect => effect.Name, effect => effect.MinLevel, StringComparer.Ordinal);
            var level = session.GetHabbo()?.GetAchievementData("ACH_CameraPhotoCount")?.Level ?? 0;
            parsed = CameraRequestParser.Parse(payload, thumbnail ? CameraChannel.Thumbnail : CameraChannel.Photo, configured, level);

            if (parsed.Status != CameraParseStatus.Accepted || parsed.Command == null) {
                Failure(session, parsed, thumbnail, false);

                return;
            }

            var state = Session(session);
            var room = session.GetHabbo()?.CurrentRoom;

            if (room == null || !Allowed(session, room, thumbnail)) {
                Failure(session, parsed, thumbnail, false);

                return;
            }

            Draft? draft = null;
            CameraViewport viewport;
            IReadOnlyList<CameraEffectSelection> effects = [];
            bool zoom = false;
            bool capturing = parsed.Command is CameraCommand.Capture;
            CancellationTokenSource generation;

            lock (state.Gate) {
                Expire(state);

                if (state.Cancellation.IsCancellationRequested) {
                    state.Cancellation = new();
                }

                generation = state.Cancellation;

                if (parsed.Command is CameraCommand.Delete delete) {
                    state.Drafts.Remove(Guid.Parse(delete.DraftId));

                    return;
                }

                if (state.RequestDay != _time.GetUtcNow().Date) {
                    state.Requests.Clear();
                    state.RequestDay = _time.GetUtcNow().Date;
                }

                if (state.Requests.Count >= 512) {
                    Failure(session, parsed, thumbnail, true);

                    return;
                }

                if (!state.Requests.Add(Guid.Parse(parsed.RequestId))) {
                    Failure(session, parsed, thumbnail, false);

                    return;
                }

                if (parsed.Command is CameraCommand.Capture capture) {
                    if (!thumbnail && state.Drafts.Count >= 5) {
                        Failure(session, parsed, false, false);

                        return;
                    }

                    viewport = capture.Viewport;
                }
                else if (parsed.Command is CameraCommand.Render render) {
                    if (!state.Drafts.TryGetValue(Guid.Parse(render.DraftId), out draft) || draft.RoomId != room.RoomId) {
                        Failure(session, parsed, thumbnail, false);

                        return;
                    }

                    viewport = draft.Viewport;
                    effects = render.Effects;
                    zoom = render.Zoom;
                }
                else {
                    return;
                }
            }

            if (effects.Count > 0) {
                var catalogue = _catalogue.Current().ToDictionary(effect => effect.Name, StringComparer.Ordinal);

                if (effects.Any(effect => !catalogue.TryGetValue(effect.Name, out var definition) || definition.MinLevel > level) ||
                    effects.Count(effect => catalogue[effect.Name].Type == "frame") > 1) {
                    Failure(session, parsed, thumbnail, false);

                    return;
                }
            }

            if (!_quota.Reserve(session.GetHabbo().Id, !capturing, thumbnail)) {
                quotaHit = true;
                Failure(session, parsed, thumbnail, true);

                return;
            }

            var scene = capturing ? JsonSerializer.SerializeToElement(CameraSnapshotBuilder.Capture(room), Json) : draft!.Scene;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(generation.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var image = await _renderer.Render(scene, viewport, effects, zoom, level, deadline.Token);

            lock (state.Gate) {
                if (!ReferenceEquals(state.Cancellation, generation) || generation.IsCancellationRequested || deadline.IsCancellationRequested || !Allowed(session, room, thumbnail)) {
                    Failure(session, parsed, thumbnail, false);

                    return;
                }

                if (thumbnail) {
                    var directory = Path.Combine(_directory, "thumbnail");
                    Directory.CreateDirectory(directory);
                    string temp = Path.Combine(directory, Guid.NewGuid() + ".tmp");
                    File.WriteAllBytes(temp, image.Png);
                    File.Move(temp, Path.Combine(directory, room.RoomId + ".png"), overwrite: true);
                    session.Send(new ThumbnailStatusComposer(true));
                }
                else {
                    if (!capturing && (draft == null || !state.Drafts.TryGetValue(draft.Id, out var current) || !ReferenceEquals(current, draft) || _time.GetUtcNow() - draft.CreatedAt >= Lifetime)) {
                        Failure(session, parsed, false, false);

                        return;
                    }

                    var mediaId = Guid.NewGuid();
                    var now = _time.GetUtcNow();
                    string path = Path.Combine(_directory, mediaId.ToString("D"));

                    try {
                        using var connection = _database.Connection();
                        connection.Execute("INSERT INTO camera_media (id,user_id,room_id,created_at) VALUES (@id,@userId,@roomId,@now)",
                            new { id = mediaId.ToString("D"), userId = session.GetHabbo().Id, roomId = room.RoomId, now = now.UtcDateTime });
                        // Persist the retry record before writing either file, including crash recovery.
                        File.WriteAllBytes(path + ".png", image.Png);
                        File.WriteAllBytes(path + "_small.png", image.SmallPng);
                    }
                    catch {
                        File.Delete(path + ".png");
                        File.Delete(path + "_small.png");
                        throw;
                    }

                    if (capturing) {
                        draft = new(Guid.NewGuid(), room.RoomId, now, scene, viewport);
                        state.Drafts.Add(draft.Id, draft);
                    }

                    draft!.Media.Add(mediaId, now);
                    var inline = image.Png.Length <= CameraStorageReply.MaxInlinePng ? image.Png : null;
                    session.Send(new CameraStorageUrlComposer(CameraStorageReply.Success(parsed.RequestId, draft.Id, mediaId, parsed.Stage, inline?.Length ?? 0), inline));
                }
            }
        }
        catch (Exception exception) {
            _logger.LogWarning(exception, "Trusted camera request failed for session {SessionId}: {Reason}", session.Id, exception.Message);
            Failure(session, parsed, thumbnail, quotaHit);
        }
    }
    private static void Failure(GameClient session, CameraParseResult? parsed, bool thumbnail, bool quota)
    {
        if (parsed?.Action == "delete" || parsed?.Command is CameraCommand.Delete) {
            return;
        }

        if (thumbnail) {
            session.Send(new ThumbnailStatusComposer(false, quota));
        }
        else {
            session.Send(new CameraStorageUrlComposer(parsed?.RequestId.Length > 0 && parsed.Stage.Length > 0
            ? CameraStorageReply.Failure(parsed.RequestId, parsed.Stage) : ""));
        }
    }
    private sealed class State
    {
        public Lock Gate { get; } = new();
        public CancellationTokenSource Cancellation { get; set; } = new();
        public Dictionary<Guid, Draft> Drafts { get; } = new();
        public HashSet<Guid> Requests { get; } = new();
        public DateTime RequestDay { get; set; }
        public uint PreparedRoomId { get; set; }
        public DateTimeOffset PreparedAt { get; set; }
    }
    private sealed record Draft(Guid Id, uint RoomId, DateTimeOffset CreatedAt, JsonElement Scene, CameraViewport Viewport)
    {
        public Dictionary<Guid, DateTimeOffset> Media { get; } = new();
    }
    public void Dispose() => _renderer.Dispose();
}
