using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plus.HabboHotel.Camera;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class CameraContextTests
{
    [Fact]
    public void LeavingDisposedRoomCancelsRendersAndRejectsItsMediaOnReentry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "camera-context-" + Guid.NewGuid());

        try {
            using var service = new CameraService(Options.Create(new CameraConfiguration { OutputDirectory = directory }),
                null!, null!, null!, TimeProvider.System, NullLogger<CameraService>.Instance);
            var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            room.Id = 42;
            var habbo = new Habbo { CurrentRoom = room, IsTeleporting = true, TeleportingRoomId = 43 };
            var (client, _) = HabbiconTestSupport.Client(habbo);
            habbo.Client = client;
            var state = typeof(CameraService).GetMethod("Session", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, [client])!;
            var cancellation = (CancellationTokenSource)state.GetType().GetProperty("Cancellation")!.GetValue(state)!;
            var drafts = (IDictionary)state.GetType().GetProperty("Drafts")!.GetValue(state)!;
            var draftType = typeof(CameraService).GetNestedType("Draft", BindingFlags.NonPublic)!;
            var draftId = Guid.NewGuid();
            var mediaId = Guid.NewGuid();
            var draft = Activator.CreateInstance(draftType, draftId, 42u, DateTimeOffset.UtcNow, default(JsonElement), null)!;
            ((Dictionary<Guid, DateTimeOffset>)draftType.GetProperty("Media")!.GetValue(draft)!).Add(mediaId, DateTimeOffset.UtcNow);
            drafts.Add(draftId, draft);
            Assert.Null(room.GetRoomUserManager());

            // The teleport mismatch returns before loading another room or touching global services.
            habbo.PrepareRoom(42, "");

            Assert.Null(habbo.CurrentRoom);
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Empty(drafts);
            habbo.CurrentRoom = room;
            var called = false;
            var result = service.Checkout(client, mediaId, _ => { called = true; return new(true, ""); });
            Assert.False(result.Ok);
            Assert.False(called);
        }
        finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void PreparationStartsANewContextAfterLeavingARoomAndOncePerRoomPerInterval()
    {
        var directory = Path.Combine(Path.GetTempPath(), "camera-context-" + Guid.NewGuid());

        try {
            var clock = new Clock();
            using var service = new CameraService(Options.Create(new CameraConfiguration { OutputDirectory = directory }),
                null!, null!, null!, clock, NullLogger<CameraService>.Instance);
            var (client, _) = HabbiconTestSupport.Client(new Habbo());
            var view = new CameraViewport(1280, 900, 0, 0, 480, 290, 320, 320, 1, 7, 7, 0);

            Assert.True(service.TryBeginPreparation(client, 42, view, out var first));
            client.EndCameraContext();
            Assert.True(first.IsCancellationRequested);

            // The next room prepares under a live context, not the one its predecessor ended.
            Assert.True(service.TryBeginPreparation(client, 43, view, out var next));
            Assert.False(next.IsCancellationRequested);
            Assert.False(service.TryBeginPreparation(client, 43, view, out _));
            clock.Now += CameraService.PreparationInterval;
            Assert.True(service.TryBeginPreparation(client, 43, view, out _));
        }
        finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ANewViewportGeometryPreparesTheRoomAgainAtMostEveryFewSeconds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "camera-context-" + Guid.NewGuid());

        try {
            var clock = new Clock();
            using var service = new CameraService(Options.Create(new CameraConfiguration { OutputDirectory = directory }),
                null!, null!, null!, clock, NullLogger<CameraService>.Instance);
            var (client, _) = HabbiconTestSupport.Client(new Habbo());
            var view = new CameraViewport(1280, 900, 0, 0, 480, 290, 320, 320, 1, 7, 7, 0);

            Assert.True(service.TryBeginPreparation(client, 42, view, out _));
            // Moving the crop within the same view is the same geometry.
            clock.Now += CameraService.GeometryInterval;
            Assert.False(service.TryBeginPreparation(client, 42, view with { X = 10, Y = 20 }, out _));
            Assert.True(service.TryBeginPreparation(client, 42, view with { LocationX = 8 }, out _));
            Assert.False(service.TryBeginPreparation(client, 42, view with { Width = 1440 }, out _));
            clock.Now += CameraService.GeometryInterval;
            Assert.True(service.TryBeginPreparation(client, 42, view with { Width = 1440 }, out _));
            Assert.True(service.TryBeginPreparation(client, 43, view, out _));
        }
        finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
