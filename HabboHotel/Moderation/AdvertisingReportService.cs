using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Help;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Moderation;

public interface IAdvertisingReportService
{
    void Submit(GameClient reporter, int targetId);
}

public sealed class AdvertisingReportService(IGameClientManager clients, TimeProvider clock) : IAdvertisingReportService
{
    public void Submit(GameClient reporter, int targetId)
    {
        var habbo = reporter.GetHabbo();

        if (targetId == habbo.Id) {
            return;
        }

        if (habbo.AdvertisingReportedBlocked) {
            reporter.Send(new SubmitBullyReportComposer(BullyReportResult.Blocked));

            return;
        }

        var target = clients.GetClientByUserId(targetId);

        if (target == null) {
            reporter.Send(new SubmitBullyReportComposer(BullyReportResult.Sent));

            return;
        }

        var now = clock.GetUtcNow();

        if (habbo.AdvertisingReportAvailableAt is { } deadline && deadline > now) {
            reporter.SendNotification("Reports can only be sent per 5 minutes!");

            return;
        }

        var reported = target.GetHabbo();

        if (reported.Access.Can(PermissionKeys.ModerationTool)) {
            reporter.SendNotification("Sorry, you cannot report staff members via this tool.");

            return;
        }

        if (!reported.HasSpoken) {
            reporter.Send(new SubmitBullyReportComposer(BullyReportResult.NoChat));

            return;
        }

        var unlimited = habbo.Access.Can(PermissionKeys.ChatReportUnlimited);

        if (reported.AdvertisingReported && !unlimited) {
            reporter.Send(new SubmitBullyReportComposer(BullyReportResult.AlreadyReported));

            return;
        }

        habbo.AdvertisingReportAvailableAt = unlimited ? now : now.AddMinutes(5);
        reported.AdvertisingReported = true;
        reporter.Send(new SubmitBullyReportComposer(BullyReportResult.Sent));
        clients.DoAdvertisingReport(reporter, target);
    }
}
