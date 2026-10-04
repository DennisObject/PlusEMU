using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Help;
using Plus.HabboHotel.GameClients;
using Plus.Utilities;

namespace Plus.Communication.Packets.Incoming.Help;

internal class SubmitBullyReportEvent : IPacketEvent
{
    private readonly IGameClientManager _clientManager;

    public SubmitBullyReportEvent(IGameClientManager clientManager)
    {
        _clientManager = clientManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        //0 = sent, 1 = blocked, 2 = no chat, 3 = already reported.
        var userId = packet.ReadInt();
        if (userId == session.GetHabbo().Id) //Hax
            return Task.CompletedTask;
        if (session.GetHabbo().AdvertisingReportedBlocked)
        {
            session.Send(new SubmitBullyReportComposer(BullyReportResult.Blocked)); //This user is blocked from reporting.
            return Task.CompletedTask;
        }
        var client = _clientManager.GetClientByUserId(Convert.ToInt32(userId));
        if (client == null)
        {
            session.Send(new SubmitBullyReportComposer(BullyReportResult.Sent)); //Just say it's sent, the user isn't found.
            return Task.CompletedTask;
        }
        if (session.GetHabbo().LastAdvertiseReport > UnixTimestamp.GetNow())
        {
            session.SendNotification("Reports can only be sent per 5 minutes!");
            return Task.CompletedTask;
        }
        if (client.GetHabbo().Access.Can(PermissionKeys.ModerationTool)) //Reporting staff, nope!
        {
            session.SendNotification("Sorry, you cannot report staff members via this tool.");
            return Task.CompletedTask;
        }

        //This user hasn't even said a word, nope!
        if (!client.GetHabbo().HasSpoken)
        {
            session.Send(new SubmitBullyReportComposer(BullyReportResult.NoChat));
            return Task.CompletedTask;
        }

        //Already reported, nope.
        if (client.GetHabbo().AdvertisingReported && !session.GetHabbo().Access.Can(PermissionKeys.ChatReportUnlimited))
        {
            session.Send(new SubmitBullyReportComposer(BullyReportResult.AlreadyReported));
            return Task.CompletedTask;
        }
        if (!session.GetHabbo().Access.Can(PermissionKeys.ChatReportUnlimited))
            session.GetHabbo().LastAdvertiseReport = UnixTimestamp.GetNow() + 300;
        else
            session.GetHabbo().LastAdvertiseReport = UnixTimestamp.GetNow();
        client.GetHabbo().AdvertisingReported = true;
        session.Send(new SubmitBullyReportComposer(BullyReportResult.Sent));
        //_clientManager.ModAlert("New advertising report! " + Client.GetHabbo().Username + " has been reported for advertising by " + Session.GetHabbo().Username +".");
        _clientManager.DoAdvertisingReport(session, client);
        return Task.CompletedTask;
    }
}