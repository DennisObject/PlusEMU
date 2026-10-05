using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Chat.Filter;

namespace Plus.HabboHotel.Catalog.Pets;

public interface IPetNameValidationService
{
    void Check(GameClient session, string name);
}

public sealed class PetNameValidationService(IWordFilterManager filter) : IPetNameValidationService
{
    public void Check(GameClient session, string name)
    {
        if (name.Length < 2)
            session.Send(new CheckPetNameComposer(PetNameError.TooShort, "2"));
        else if (name.Length > 15)
            session.Send(new CheckPetNameComposer(PetNameError.TooLong, "15"));
        else if (!PetUtility.CheckPetName(name))
            session.Send(new CheckPetNameComposer(PetNameError.InvalidCharacters, ""));
        else if (filter.IsFiltered(name))
            session.Send(new CheckPetNameComposer(PetNameError.Filtered, ""));
        else
            session.Send(new CheckPetNameComposer(PetNameError.None, ""));
    }
}
