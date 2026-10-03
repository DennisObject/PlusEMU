using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Incoming.FurniEditor;

public class FurniEditorImportTextEvent : IPacketEvent
{
    private readonly IFurniEditorService _furniEditor;
    private readonly ILogger<FurniEditorImportTextEvent> _logger;

    public FurniEditorImportTextEvent(IFurniEditorService furniEditor, ILogger<FurniEditorImportTextEvent> logger)
    {
        _furniEditor = furniEditor;
        _logger = logger;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        uint id = packet.ReadUInt();
        var habbo = session.GetHabbo();
        FurniEditorResponder.InBackground(session, _logger, async () => new FurniEditorImportTextResultComposer(await _furniEditor.ImportText(habbo, id)));
        return Task.CompletedTask;
    }
}
