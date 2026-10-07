using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Games.SnowStorm;

/// <summary>
/// SnowStorm lobbies and matches. Packet handlers only queue requests here; <see cref="Tick"/> applies them, advances
/// lobbies and arenas and sends every reply, so game state is only touched by the ticker.
/// </summary>
public interface ISnowStormManager
{
    void QuickJoin(GameClient session);

    void LeaveLobby(GameClient session);

    void LoadStageReady(GameClient session);

    void ExitGame(GameClient session);

    void PlayAgain(GameClient session);

    void Chat(GameClient session, string message);

    /// <summary>Votes for the arena (field type) the player's lobby will play on.</summary>
    void VoteArena(GameClient session, int fieldType);

    void SetMoveTarget(GameClient session, int x, int y, int turn, int subturn);

    void ThrowAtPosition(GameClient session, int x, int y, int trajectory, int turn, int subturn);

    void ThrowAtHuman(GameClient session, int targetHumanId, int trajectory, int turn, int subturn);

    void MakeSnowball(GameClient session, int turn, int subturn);

    void RequestFullStatus(GameClient session, int reason);

    /// <summary>Seconds the user is still blocked from joining after leaving a running game.</summary>
    int BlockSeconds(int userId);

    /// <summary>Applies queued requests and advances lobbies and games to the current time.</summary>
    void Tick();
}
