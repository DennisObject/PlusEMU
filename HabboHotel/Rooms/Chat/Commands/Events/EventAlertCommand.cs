using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Events;

internal class EventAlertCommand : IChatCommand
{
    private readonly IGameClientManager _gameClientManager;
    private readonly TimeProvider _clock;
    public string Key => "eha";

    public string Parameters => "";

    public string Description => "Send a hotel alert for your event!";

    private static readonly object EventSync = new();
    private static DateTimeOffset? _lastEvent;

    public EventAlertCommand(IGameClientManager gameClientManager, TimeProvider clock)
    {
        _gameClientManager = gameClientManager;
        _clock = clock;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        lock (EventSync)
        {
            var now = _clock.GetUtcNow();

            if (_lastEvent == null || now - _lastEvent > TimeSpan.FromHours(1))
            {
                _gameClientManager.SendPacket(new BroadcastMessageAlertComposer($":follow {session.GetHabbo().Username} for events! win prizes!\r\n- {session.GetHabbo().Username}"));
                _lastEvent = now;
            }
            else
            {
                session.SendWhisper($"Event Cooldown! {(now - _lastEvent).Value.Minutes} minutes left until another event can be hosted.");
            }
        }
    }
}
