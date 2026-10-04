using Plus.Database;
using Plus.Core.FigureData;
using Plus.HabboHotel.Items.Wired.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

internal class SaveWiredConditionConfigEvent(IDatabase database, IFigureDataManager figures, ILogger<SaveWiredConfigEvent> logger) : SaveWiredConfigEvent(database, figures, logger)
{
    public SaveWiredConditionConfigEvent(IDatabase database, IFigureDataManager figures)
        : this(database, figures, NullLogger<SaveWiredConfigEvent>.Instance) { }
    protected override WiredBoxCategory Envelope => WiredBoxCategory.Condition;
}
