using System.Globalization;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal abstract class RoleCommand(IDatabase database, IAccessControl access, TimeProvider clock) : IChatCommand
{
    public abstract string Key
    {
        get;
    }
    public abstract string Parameters
    {
        get;
    }
    public abstract string Description
    {
        get;
    }
    protected abstract bool Assign
    {
        get;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var actor = session.GetHabbo();

        if (!access.Can(actor.Id, "command." + Key) || !access.Can(actor.Id, PermissionKeys.HousekeepingRolesManage))
        {
            session.SendWhisper("You are not allowed to manage roles.");

            return;
        }

        if (parameters.Length < 2 || parameters.Length > (Assign ? 3 : 2) || parameters.Take(2).Any(string.IsNullOrWhiteSpace))
        {
            session.SendWhisper($"Usage: :{Key} {Parameters}");

            return;
        }

        DateTimeOffset? expiresAt = null;

        if (Assign && parameters.Length == 3)
        {
            if (!int.TryParse(parameters[2], NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days is < 1 or > 3650)
            {
                session.SendWhisper("Invalid days: enter a whole number from 1 to 3650, or omit days for a permanent role.");

                return;
            }

            expiresAt = clock.GetUtcNow().AddDays(days);
        }

        using var connection = database.Connection();
        var targetId = connection.QuerySingleOrDefault<int>("SELECT id FROM users WHERE username = @username LIMIT 1", new
        {
            username = parameters[0]
        });

        if (targetId == 0)
        {
            session.SendWhisper($"Unknown user '{parameters[0]}'.");

            return;
        }

        var roleId = connection.QuerySingleOrDefault<int>("SELECT id FROM roles WHERE slug = @slug LIMIT 1", new
        {
            slug = parameters[1]
        });

        if (!access.TryGetRole(roleId, out var role))
        {
            var roles = connection.Query<int>("SELECT id FROM roles ORDER BY slug")
                .Select(id => access.TryGetRole(id, out var candidate) ? candidate : null).OfType<AccessRole>().ToArray();
            var registry = PermissionKeys.All.Concat(PermissionKeys.ForRoles(roles.Select(candidate => candidate.Slug))).Select(definition => definition.Key);
            var actorAccess = access.Resolve(actor.Id);
            var targetAccess = access.Resolve(targetId);
            var slugs = roles.Where(candidate => candidate.Slug != "default" &&
                AccessMutationPolicy.CanAssign(actor.Id, targetId, actorAccess, targetAccess, candidate, registry)).Select(candidate => candidate.Slug).ToArray();
            session.SendWhisper($"Unknown role '{parameters[1]}'. Roles you may assign: {(slugs.Length == 0 ? "none" : string.Join(", ", slugs))}.");

            return;
        }

        var succeeded = Assign ? access.AssignRole(actor, targetId, role.Id, expiresAt) : access.RevokeRole(actor, targetId, role.Id);

        if (!succeeded)
        {
            session.SendWhisper("Role change refused: you may only edit lower-weight users and roles within your access; self-edit and the default role are forbidden. Revoking also requires an existing assignment.");

            return;
        }

        session.SendWhisper(Assign
            ? $"Assigned role '{role.Slug}' to {parameters[0]} ({(expiresAt.HasValue ? $"expires {expiresAt.Value:yyyy-MM-dd HH:mm:ss} UTC" : "permanent")})."
            : $"Revoked role '{role.Slug}' from {parameters[0]}.");
    }
}
