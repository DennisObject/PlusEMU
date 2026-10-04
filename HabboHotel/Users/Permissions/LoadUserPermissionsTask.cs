using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users.UserData;

namespace Plus.HabboHotel.Users.Permissions;

internal sealed class LoadUserPermissionsTask(IAccessControl accessControl) : IUserDataLoadingTask
{
    public Task Load(Habbo habbo)
    {
        habbo.Access = accessControl.Resolve(habbo.Id);
        return Task.CompletedTask;
    }
}
