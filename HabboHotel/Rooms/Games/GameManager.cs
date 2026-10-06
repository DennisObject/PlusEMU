using System.Collections.Concurrent;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.Games.Teams;

namespace Plus.HabboHotel.Rooms.Games;

public class GameManager
{
    private ConcurrentDictionary<uint, Item> _blueTeamItems;
    private ConcurrentDictionary<uint, Item> _greenTeamItems;
    private ConcurrentDictionary<uint, Item> _redTeamItems;
    private ConcurrentDictionary<uint, Item> _yellowTeamItems;
    private Room _room;
    private readonly TimeProvider _clock;

    public GameManager(Room room, TimeProvider clock)
    {
        _room = room;
        _clock = clock;
        Points = new int[5];
        _redTeamItems = new();
        _blueTeamItems = new();
        _greenTeamItems = new();
        _yellowTeamItems = new();
    }

    public int[] Points { get; set; }

    public Team GetWinningTeam()
    {
        var winning = 1;
        var highestScore = 0;

        for (var i = 1; i < 5; i++) {
            if (Points[i] > highestScore) {
                highestScore = Points[i];
                winning = i;
            }
        }

        return (Team)winning;
    }

    public void AddPointToTeam(Team team, int points)
    {
        var newPoints = Points[Convert.ToInt32(team)] += points;

        if (newPoints < 0) {
            newPoints = 0;
        }

        Points[Convert.ToInt32(team)] = newPoints;

        foreach (var item in GetFurniItems(team).Values.ToList()) {
            if (!IsFootballGoal(item.Definition.InteractionType)) {
                item.LegacyDataString = Points[Convert.ToInt32(team)].ToString();
                item.UpdateState();
            }
        }

        foreach (var item in _room.GetRoomItemHandler().GetFloor.ToList()) {
            if (team == Team.Blue && item.Definition.InteractionType == InteractionType.Banzaiscoreblue) {
                item.LegacyDataString = Points[Convert.ToInt32(team)].ToString();
                item.UpdateState();
            }
            else if (team == Team.Red && item.Definition.InteractionType == InteractionType.Banzaiscorered) {
                item.LegacyDataString = Points[Convert.ToInt32(team)].ToString();
                item.UpdateState();
            }
            else if (team == Team.Green && item.Definition.InteractionType == InteractionType.Banzaiscoregreen) {
                item.LegacyDataString = Points[Convert.ToInt32(team)].ToString();
                item.UpdateState();
            }
            else if (team == Team.Yellow && item.Definition.InteractionType == InteractionType.Banzaiscoreyellow) {
                item.LegacyDataString = Points[Convert.ToInt32(team)].ToString();
                item.UpdateState();
            }
        }
    }

    public void Reset()
    {
        AddPointToTeam(Team.Blue, GetScoreForTeam(Team.Blue) * -1);
        AddPointToTeam(Team.Green, GetScoreForTeam(Team.Green) * -1);
        AddPointToTeam(Team.Red, GetScoreForTeam(Team.Red) * -1);
        AddPointToTeam(Team.Yellow, GetScoreForTeam(Team.Yellow) * -1);
    }

    private int GetScoreForTeam(Team team) => Points[Convert.ToInt32(team)];

    private ConcurrentDictionary<uint, Item> GetFurniItems(Team team)
    {
        switch (team) {
            default:
                return new();
            case Team.Blue:
                return _blueTeamItems;
            case Team.Green:
                return _greenTeamItems;
            case Team.Red:
                return _redTeamItems;
            case Team.Yellow:
                return _yellowTeamItems;
        }
    }

    private static bool IsFootballGoal(InteractionType type) => type == InteractionType.FootballGoalBlue || type == InteractionType.FootballGoalGreen || type == InteractionType.FootballGoalRed ||
                                                                type == InteractionType.FootballGoalYellow;

    public void AddFurnitureToTeam(Item item, Team team)
    {
        switch (team) {
            case Team.Blue:
                _blueTeamItems.TryAdd(item.Id, item);
                break;
            case Team.Green:
                _greenTeamItems.TryAdd(item.Id, item);
                break;
            case Team.Red:
                _redTeamItems.TryAdd(item.Id, item);
                break;
            case Team.Yellow:
                _yellowTeamItems.TryAdd(item.Id, item);
                break;
        }
    }

    public void RemoveFurnitureFromTeam(Item item, Team team)
    {
        switch (team) {
            case Team.Blue:
                _blueTeamItems.TryRemove(item.Id, out item);
                break;
            case Team.Green:
                _greenTeamItems.TryRemove(item.Id, out item);
                break;
            case Team.Red:
                _redTeamItems.TryRemove(item.Id, out item);
                break;
            case Team.Yellow:
                _yellowTeamItems.TryRemove(item.Id, out item);
                break;
        }
    }

    public void LockGates()
    {
        foreach (var item in _redTeamItems.Values.ToList()) {
            LockGate(item);
        }

        foreach (var item in _greenTeamItems.Values.ToList()) {
            LockGate(item);
        }

        foreach (var item in _blueTeamItems.Values.ToList()) {
            LockGate(item);
        }

        foreach (var item in _yellowTeamItems.Values.ToList()) {
            LockGate(item);
        }
    }

    public void UnlockGates()
    {
        foreach (var item in _redTeamItems.Values.ToList()) {
            UnlockGate(item);
        }

        foreach (var item in _greenTeamItems.Values.ToList()) {
            UnlockGate(item);
        }

        foreach (var item in _blueTeamItems.Values.ToList()) {
            UnlockGate(item);
        }

        foreach (var item in _yellowTeamItems.Values.ToList()) {
            UnlockGate(item);
        }
    }

    private void LockGate(Item item)
    {
        var type = item.Definition.InteractionType;

        if (type == InteractionType.FreezeBlueGate || type == InteractionType.FreezeGreenGate ||
            type == InteractionType.FreezeRedGate || type == InteractionType.FreezeYellowGate
            || type == InteractionType.Banzaigateblue || type == InteractionType.Banzaigatered ||
            type == InteractionType.Banzaigategreen || type == InteractionType.Banzaigateyellow) {
            foreach (var user in _room.GetGameMap().GetRoomUsers(new(item.GetX, item.GetY))) {
                user.SqState = 0;
            }

            _room.GetGameMap().SetFloorStatus(item.GetX, item.GetY, 0);
        }
    }

    private void UnlockGate(Item item)
    {
        var type = item.Definition.InteractionType;

        if (type == InteractionType.FreezeBlueGate || type == InteractionType.FreezeGreenGate ||
            type == InteractionType.FreezeRedGate || type == InteractionType.FreezeYellowGate
            || type == InteractionType.Banzaigateblue || type == InteractionType.Banzaigatered ||
            type == InteractionType.Banzaigategreen || type == InteractionType.Banzaigateyellow) {
            foreach (var user in _room.GetGameMap().GetRoomUsers(new(item.GetX, item.GetY))) {
                user.SqState = 1;
            }

            _room.GetGameMap().SetFloorStatus(item.GetX, item.GetY, 1);
        }
    }

    public void StopGame()
    {
        StopGame(_clock.GetUtcNow());
    }

    internal void StopGame(DateTimeOffset stoppedAt) => _room.LastTimerResetAt = stoppedAt;

    public void Dispose()
    {
        Array.Clear(Points, 0, Points.Length);
        _redTeamItems.Clear();
        _blueTeamItems.Clear();
        _greenTeamItems.Clear();
        _yellowTeamItems.Clear();
        Points = null;
        _redTeamItems = null;
        _blueTeamItems = null;
        _greenTeamItems = null;
        _yellowTeamItems = null;
        _room = null;
    }
}
