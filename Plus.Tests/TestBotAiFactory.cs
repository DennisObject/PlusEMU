using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Tests;

internal sealed class TestBotAiFactory(Func<BotAiType, int, BotAi>? create = null) : IBotAiFactory
{
    public static IBotAiFactory Inert { get; } = new TestBotAiFactory((_, _) => new InertBotAi());

    public BotAi Create(BotAiType type, int virtualId) => create?.Invoke(type, virtualId)
        ?? throw new InvalidOperationException("Unexpected bot AI creation.");

    private sealed class InertBotAi : BotAi
    {
        public override void OnSelfEnterRoom() { }
        public override void OnSelfLeaveRoom(bool kicked) { }
        public override void OnUserEnterRoom(RoomUser user) { }
        public override void OnUserLeaveRoom(GameClient client) { }
        public override void OnUserSay(RoomUser user, string message) { }
        public override void OnUserShout(RoomUser user, string message) { }
        public override void OnTimerTick() { }
    }
}
