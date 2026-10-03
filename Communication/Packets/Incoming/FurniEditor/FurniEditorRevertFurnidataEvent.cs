using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Incoming.FurniEditor;

public class FurniEditorRevertFurnidataEvent : IPacketEvent
{
    private readonly IFurniEditorService _furniEditor;
    private readonly ILogger<FurniEditorRevertFurnidataEvent> _logger;

    public FurniEditorRevertFurnidataEvent(IFurniEditorService furniEditor, ILogger<FurniEditorRevertFurnidataEvent> logger)
    {
        _furniEditor = furniEditor;
        _logger = logger;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        uint id = packet.ReadUInt();
        var habbo = session.GetHabbo();
        FurniEditorResponder.InBackground(session, _logger, () => Task.FromResult<IServerPacket>(new FurniEditorResultComposer(_furniEditor.RevertFurnidata(habbo, id))));
        return Task.CompletedTask;
    }
}
