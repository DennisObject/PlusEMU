using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Incoming.FurniEditor;

public class FurniEditorUpdateFurnidataEvent : IPacketEvent
{
    private readonly IFurniEditorService _furniEditor;
    private readonly ILogger<FurniEditorUpdateFurnidataEvent> _logger;

    public FurniEditorUpdateFurnidataEvent(IFurniEditorService furniEditor, ILogger<FurniEditorUpdateFurnidataEvent> logger)
    {
        _furniEditor = furniEditor;
        _logger = logger;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        uint id = packet.ReadUInt();
        string json = packet.ReadString();
        var habbo = session.GetHabbo();
        FurniEditorResponder.InBackground(session, _logger, id, () => Task.FromResult<IServerPacket>(new FurniEditorResultComposer(_furniEditor.UpdateFurnidata(habbo, id, json))));
        return Task.CompletedTask;
    }
}
