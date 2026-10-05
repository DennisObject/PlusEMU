using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

internal class SaveWiredConditionConfigEvent(IWiredConfigurationService service) : SaveWiredConfigEvent(service)
{
    protected override WiredBoxCategory Envelope => WiredBoxCategory.Condition;
}
