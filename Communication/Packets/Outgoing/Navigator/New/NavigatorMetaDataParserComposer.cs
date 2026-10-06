using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator.New;

// TODO @80O: Implement
public class NavigatorMetaDataParserComposer(ImmutableArray<string> searchCodes) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.NavigatorMetaDataParserComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(searchCodes.Length); //Count

        foreach (var searchCode in searchCodes) {
            //TopLevelContext
            packet.WriteString(searchCode); //Search code
            packet.WriteInteger(0); //Count of saved searches?
            /*{
                //SavedSearch
                base.WriteInteger(TopLevelItem.Id);//Id
               base.WriteString(TopLevelItem.SearchCode);//Search code
               base.WriteString(TopLevelItem.Filter);//Filter
               base.WriteString(TopLevelItem.Localization);//localization
            }*/
        }
    }
}
