using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.UserData;

namespace Plus.HabboHotel.Users.Permissions;

internal sealed class LoadUserPermissionsTask(IAccessControl accessControl) : IUserDataLoadingTask, IAuthenticationTask
{
    public Task Load(Habbo habbo)
    {
        habbo.Access = accessControl.Resolve(habbo.Id);

        return Task.CompletedTask;
    }

    public Task UserLoggedIn(Habbo habbo)
    {
        // Loading happens before attachment; re-read once registered to cover mutations during login.
        habbo.Access = accessControl.Resolve(habbo.Id);

        return Task.CompletedTask;
    }
}
