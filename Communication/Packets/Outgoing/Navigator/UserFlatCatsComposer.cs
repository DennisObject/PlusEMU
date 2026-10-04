using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class UserFlatCatsComposer : IServerPacket
{
    private readonly IReadOnlyCollection<SearchResultList> _categories;
    private readonly UserAccess _access;

    public uint MessageId => ServerPacketHeader.UserFlatCatsComposer;

    public UserFlatCatsComposer(IReadOnlyCollection<SearchResultList> categories, UserAccess access)
    {
        _categories = categories;
        _access = access;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_categories.Count);
        foreach (var category in _categories)
        {
            packet.WriteInteger(category.Id);
            packet.WriteString(category.PublicName);
            packet.WriteBoolean(category.RequiredPermission.Length == 0 || _access.Can(category.RequiredPermission));
            packet.WriteBoolean(false);
            packet.WriteString(string.Empty);
            packet.WriteString(string.Empty);
            packet.WriteBoolean(false);
        }
    }
}