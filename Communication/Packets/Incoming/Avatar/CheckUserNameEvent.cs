using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users.UserData;

namespace Plus.Communication.Packets.Incoming.Avatar;

internal class CheckUserNameEvent : IPacketEvent
{
    private readonly IUserDataFactory _userDataFactory;
    private readonly IWordFilterManager _wordFilterManager;
    private readonly IDatabase _database;

    public CheckUserNameEvent(IUserDataFactory userDataFactory, IWordFilterManager wordFilterManager, IDatabase database)
    {
        _userDataFactory = userDataFactory;
        _wordFilterManager = wordFilterManager;
        _database = database;
    }

    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        var name = packet.ReadString();
        var inUse = await _userDataFactory.HabboExists(name);
        var letters = name.ToLower().ToCharArray();
        const string allowedCharacters = "abcdefghijklmnopqrstuvwxyz.,_-;:?!1234567890";
        if (letters.Any(chr => !allowedCharacters.Contains(chr)))
        {
            session.Send(new NameChangeUpdateComposer(name, NameChangeError.InvalidName));
            return;
        }
        if (_wordFilterManager.IsFiltered(name))
        {
            session.Send(new NameChangeUpdateComposer(name, NameChangeError.InvalidName));
            return;
        }
        if (!session.GetHabbo().Access.Can(PermissionKeys.ModerationTool) && name.ToLower().Contains("mod") || name.ToLower().Contains("adm") || name.ToLower().Contains("admin") ||
            name.ToLower().Contains("m0d"))
        {
            session.Send(new NameChangeUpdateComposer(name, NameChangeError.InvalidName));
            return;
        }
        if (!name.ToLower().Contains("mod") && session.GetHabbo().Access.Can(PermissionKeys.AvatarNameStaffPrefixRequired))
        {
            session.Send(new NameChangeUpdateComposer(name, NameChangeError.InvalidName));
            return;
        }
        if (name.Length > 15)
        {
            session.Send(new NameChangeUpdateComposer(name, NameChangeError.TooLong));
            return;
        }
        if (name.Length < 3)
        {
            session.Send(new NameChangeUpdateComposer(name, NameChangeError.TooShort));
            return;
        }
        if (inUse)
        {
            ICollection<string> suggestions = new List<string>();
            for (var i = 100; i < 103; i++) suggestions.Add(i.ToString());
            session.Send(new NameChangeUpdateComposer(name, NameChangeError.InUse, suggestions));
            return;
        }
        session.Send(new NameChangeUpdateComposer(name, NameChangeError.None));
        return;
    }
}