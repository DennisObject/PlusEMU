using Plus.Database;
using Plus.Core.FigureData;
using Plus.HabboHotel.Items.Wired.Configuration;
using Microsoft.Extensions.Logging;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

internal class SaveWiredConditionConfigEvent(IDatabase database, IFigureDataManager figures, ILogger<SaveWiredConfigEvent> logger) : SaveWiredConfigEvent(database, figures, logger)
{
    protected override WiredBoxCategory Envelope => WiredBoxCategory.Condition;
}
