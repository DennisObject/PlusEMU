using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Users.Messenger;

public class MessengerBuddy
{
    [Obsolete("Should be removed")]
    public GameClient? Client;
    private Habbo? _habbo;

    public Habbo? Habbo
    {
        get => _habbo;
        set
        {
            _habbo = value;

            if (_habbo != null) {
                Look = _habbo.Look;
                Motto = _habbo.Motto;
                Gender = _habbo.Gender.Equals("M", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }
        }
    }

    public bool AppearOffline => _habbo == null;

    public bool HideInRoom => _habbo?.AllowUserFollowing ?? true;
    public DateTimeOffset? LastOnlineAt { get; set; }
    public string Look { get; set; } = string.Empty;
    public string Motto { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int Relationship { get; set; }
    public int Gender { get; set; }
    public int Id { get; set; }

    public bool InRoom => CurrentRoom != null;

    public Room? CurrentRoom { get; set; }
}
