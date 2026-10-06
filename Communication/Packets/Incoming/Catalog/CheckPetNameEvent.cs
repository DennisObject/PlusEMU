using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Chat.Filter;

namespace Plus.Communication.Packets.Incoming.Catalog;

public class CheckPetNameEvent : IPacketEvent
{
    private readonly IWordFilterManager _wordFilterManager;

    public CheckPetNameEvent(IWordFilterManager wordFilterManager)
    {
        _wordFilterManager = wordFilterManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var petName = packet.ReadString();
        if (petName.Length < 2)
        {
            session.Send(new CheckPetNameComposer(PetNameError.TooShort, "2"));
            return Task.CompletedTask;
        }
        if (petName.Length > 15)
        {
            session.Send(new CheckPetNameComposer(PetNameError.TooLong, "15"));
            return Task.CompletedTask;
        }
        if (!PetUtility.CheckPetName(petName))
        {
            session.Send(new CheckPetNameComposer(PetNameError.InvalidCharacters, string.Empty));
            return Task.CompletedTask;
        }

        if (_wordFilterManager.IsFiltered(petName))
        {
            session.Send(new CheckPetNameComposer(PetNameError.Filtered, string.Empty));
            return Task.CompletedTask;
        }
        session.Send(new CheckPetNameComposer(PetNameError.None, string.Empty));
        return Task.CompletedTask;
    }
}