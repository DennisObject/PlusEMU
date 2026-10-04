using Plus.Database;
using Plus.Core.FigureData;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

internal class SaveWiredConditionConfigEvent(IDatabase database, IFigureDataManager figures) : SaveWiredConfigEvent(database, figures)
{
    protected override WiredBoxCategory Envelope => WiredBoxCategory.Condition;
}
