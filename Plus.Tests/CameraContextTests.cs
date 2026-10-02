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
        try
        {
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
            var draftId = Guid.NewGuid(); var mediaId = Guid.NewGuid();
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
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
