using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Database;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Permissions;
using Plus.HabboHotel.Users.UserData;
using Xunit;

namespace Plus.Tests;

public sealed class AccessControlLoginRegistrationTests
{
    [Fact]
    public async Task ProductionScanResolvesPermissionLoadingAndPostRegistrationAuthenticationHook()
    {
        var clients = new GameClientManager(EditorTestSupport.UntouchableDatabase(), NullLogger<GameClientManager>.Instance);
        var access = DispatchProxy.Create<IAccessControl, RecordingAccess>();
        var recorder = (RecordingAccess)access;
        recorder.Clients = clients;
        recorder.Snapshot = EditorTestSupport.Access([PermissionKeys.CameraUse]);
        var services = new ServiceCollection();
        services.AddSingleton(access);
        services.AddSingleton<IGameClientManager>(clients);
        services.AddSingleton(EditorTestSupport.UntouchableDatabase());
        services.AddSingleton(DispatchProxy.Create<IModerationManager, Untouchable>());
        services.AddSingleton(DispatchProxy.Create<IUserDataFactory, Untouchable>());
        services.AddSingleton(DispatchProxy.Create<IMessengerDataLoader, Untouchable>());
        // Use the production scanner, including its interface-to-self factories and internal class discovery.
        Plus.Program.AddAssignableTo<IAuthenticationTask>(services, typeof(Plus.Program).Assembly);
        using var provider = services.BuildServiceProvider();
        var loader = Assert.Single(provider.GetServices<IUserDataLoadingTask>());
        var hook = Assert.Single(provider.GetServices<IAuthenticationTask>().OfType<LoadUserPermissionsTask>());
        Assert.Same(loader, hook);
        var habbo = new Habbo { Id = 41001, Username = "login_access" };
        var (session, _) = HabbiconTestSupport.Client(null!);
        session.Id = Guid.NewGuid();

        await loader.Load(habbo);
        var originallyLoaded = habbo.Access;
        Assert.True(originallyLoaded.Can(PermissionKeys.CameraUse));
        recorder.Snapshot = UserAccess.Empty; // A role/override change made while login is loading.
        Assert.True(session.TryAttach(habbo, () => clients.RegisterClient(session, habbo.Id, habbo.Username)));
        await hook.UserLoggedIn(habbo);

        Assert.Equal(new[] { false, true }, recorder.RegisteredAtResolution);
        Assert.NotSame(originallyLoaded, habbo.Access);
        Assert.False(habbo.Access.Can(PermissionKeys.CameraUse));
    }

    public class RecordingAccess : DispatchProxy
    {
        public IGameClientManager Clients { get; set; } = null!;
        public UserAccess Snapshot { get; set; } = UserAccess.Empty;
        public List<bool> RegisteredAtResolution { get; } = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IAccessControl.Resolve)) throw new InvalidOperationException(method?.Name);
            RegisteredAtResolution.Add(Clients.GetClientByUserId((int)args![0]!) != null);
            return Snapshot;
        }
    }

    public class Untouchable : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException(method?.Name);
    }
}
