using Plus.Database;
using Plus.Core.FigureData;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

internal class SaveWiredEffectConfigEvent(IDatabase database, IFigureDataManager figures) : SaveWiredConfigEvent(database, figures)
{
    protected override WiredBoxCategory Envelope => WiredBoxCategory.Action;
}
