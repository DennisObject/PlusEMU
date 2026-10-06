using System.Diagnostics.CodeAnalysis;
namespace Plus.HabboHotel.Games;

public interface IGameDataManager
{
    ICollection<GameData> GameData { get; }
    void Init();
    bool TryGetGame(int gameId, [NotNullWhen(true)] out GameData? data);
    int GetCount();
}
