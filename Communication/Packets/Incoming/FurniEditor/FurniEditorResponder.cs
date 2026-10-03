using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Incoming.FurniEditor;

internal static class FurniEditorResponder
{
    public static void Read(GameClient session, Func<IServerPacket> read)
    {
        try
        {
            session.Send(read());
        }
        catch (FurniEditorRejected rejected)
        {
            session.Send(new FurniEditorResultComposer(new(false, rejected.Message)));
        }
    }

    // Furnidata writes and the Habbo import can outlast the packet deadline, so they answer when they are done.
    public static void InBackground(GameClient session, ILogger logger, Func<Task<IServerPacket>> work) => _ = Task.Run(async () =>
    {
        IServerPacket answer;
        try
        {
            answer = await work();
        }
        catch (FurniEditorRejected rejected)
        {
            answer = new FurniEditorResultComposer(new(false, rejected.Message));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Furni editor request failed");
            answer = new FurniEditorResultComposer(new(false, "The server could not finish this request"));
        }
        session.Send(answer);
    });
}
