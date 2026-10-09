using System.Reflection;
using Plus.Core.Settings;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

public sealed class LoadUserMessengerTaskTests
{
    [Fact]
    public async Task LoadedMessengerUsesItsInjectedSettingsAndObservesLaterAccessAndLimitChanges()
    {
        var settings = new MutableSettings();
        settings.Values["club.limit.friends.normal"] = "7";
        settings.Values["club.limit.friends.member"] = "9";
        var loader = DispatchProxy.Create<IMessengerDataLoader, LoaderProxy>();
        var habbo = new Habbo { Id = 1 };

        await new LoadUserMessengerTask(loader, TimeProvider.System, settings).Load(habbo);

        Assert.Equal(2, Assert.Single(Assert.IsType<HabboMessenger>(habbo.Messenger).Friends).Key);
        Assert.Equal(3, Assert.Single(habbo.Messenger.Requests).Key);
        Assert.Equal(4, Assert.Single(habbo.Messenger.OutstandingFriendRequests));
        Assert.Equal(7, habbo.Messenger.FriendLimit());
        settings.Values["club.limit.friends.normal"] = "12";
        Assert.Equal(12, habbo.Messenger.FriendLimit());
        habbo.Access = UserAccess.Create([], [new(PermissionKeys.ClubAccess, false)]);
        Assert.Equal(9, habbo.Messenger.FriendLimit());
        settings.Values["club.limit.friends.member"] = "20";
        Assert.Equal(20, habbo.Messenger.FriendLimit());
        habbo.Access = UserAccess.Empty;
        Assert.Equal(12, habbo.Messenger.FriendLimit());
        settings.Values.Clear();
        Assert.Equal(300, habbo.Messenger.FriendLimit());
    }

    public class LoaderProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(1, Assert.Single(args!));

            return targetMethod!.Name switch
            {
                nameof(IMessengerDataLoader.GetBuddiesForUser) => Task.FromResult(new List<MessengerBuddy> { new() { Id = 2 } }),
                nameof(IMessengerDataLoader.GetRequestsForUser) => Task.FromResult(new List<MessengerRequest> { new() { FromId = 3, ToId = 1 } }),
                nameof(IMessengerDataLoader.GetOutstandingRequestsForUser) => Task.FromResult(new List<int> { 4 }),
                _ => throw new InvalidOperationException(targetMethod.Name),
            };
        }
    }

    private sealed class MutableSettings : ISettingsManager
    {
        public Dictionary<string, string> Values { get; } = [];
        public string TryGetValue(string key) => Values.GetValueOrDefault(key, "0");
        public string? GetOptionalValue(string key) => Values.GetValueOrDefault(key);
        public Task Reload() => Task.CompletedTask;
    }
}
