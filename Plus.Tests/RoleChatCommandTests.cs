using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Database;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Rooms.Chat.Commands.Moderator;
using Xunit;

namespace Plus.Tests;

public sealed class RoleChatCommandTests
{
    [Fact]
    public void ProductionCommandRegistrationDiscoversBothRoleCommands()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDatabase>(EditorTestSupport.UntouchableDatabase());
        services.AddSingleton<IAccessControl>(provider => new AccessControl(provider.GetRequiredService<IDatabase>(), null!, NullLogger<AccessControl>.Instance, TimeProvider.System));
        services.AddSingleton(TimeProvider.System);
        Program.AddAssignableTo<ICommandBase>(services, typeof(GiveRoleCommand).Assembly);
        using var provider = services.BuildServiceProvider();
        var give = provider.GetRequiredService<GiveRoleCommand>();
        var take = provider.GetRequiredService<TakeRoleCommand>();
        var commands = new CommandManager([give, take], null!, provider.GetRequiredService<IDatabase>());
        Assert.True(commands.TryGetCommand("giverole", out var registeredGive));
        Assert.Same(give, registeredGive);
        Assert.True(commands.TryGetCommand("takerole", out var registeredTake));
        Assert.Same(take, registeredTake);
        Assert.Contains(PermissionKeys.All, definition => definition.Key == PermissionKeys.CommandGiverole);
        Assert.Contains(PermissionKeys.All, definition => definition.Key == PermissionKeys.CommandTakerole);
    }
}
