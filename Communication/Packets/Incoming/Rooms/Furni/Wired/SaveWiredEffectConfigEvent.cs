using Plus.Database;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

internal class SaveWiredEffectConfigEvent(IDatabase database) : SaveWiredConfigEvent(database)
{
    protected override WiredBoxCategory Envelope => WiredBoxCategory.Action;
}
