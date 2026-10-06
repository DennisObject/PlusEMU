using System.Diagnostics.CodeAnalysis;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.HabboHotel.GameClients;

public interface IGameClientManager
{
    int Count { get; }
    ICollection<GameClient> GetClients { get; }
    void OnCycle();
    GameClient? GetClientByUserId(int userId);
    GameClient? GetClientByUsername(string username);
    bool TryGetClient(Guid clientId, [NotNullWhen(true)] out GameClient? client);
    bool TryChangeClientUsername(GameClient client, string oldUsername, string newUsername, Func<bool> persist);
    Task<string> GetNameById(int id);
    IEnumerable<GameClient> GetClientsById(Dictionary<int, MessengerBuddy>.KeyCollection users);
    void StaffAlert(IServerPacket message, int exclude = 0);
    void ModAlert(string message);
    void DoAdvertisingReport(GameClient reporter, GameClient target);
    void SendPacket(IServerPacket packet, PermissionDefinition? permission = null);
    void LogClonesOut(int userId);
    void RegisterClient(GameClient client, int userId, string username);
    void UnregisterClient(GameClient client, int userId, string username);
    void CloseAll();
}
