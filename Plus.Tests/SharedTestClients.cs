using Plus.Communication.Packets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Tests;

internal sealed class SharedTestClients : IGameClientManager
{
    public Dictionary<int, GameClient> Online { get; } = new();
    public List<IServerPacket> Broadcasts { get; } = new();
    public int Count => Online.Count;
    public ICollection<GameClient> GetClients => Online.Values;
    public GameClient? GetClientByUserId(int userId) => Online.GetValueOrDefault(userId);
    public GameClient? GetClientByUsername(string username) => Online.Values.FirstOrDefault(client => client.GetHabbo().Username == username);
    public void SendPacket(IServerPacket packet, PermissionDefinition? permission = null) => Broadcasts.Add(packet);
    public void OnCycle() { }
    public bool TryGetClient(Guid clientId, out GameClient client) => throw new NotSupportedException();
    public bool TryChangeClientUsername(GameClient client, string oldUsername, string newUsername, Func<bool> persist) => throw new NotSupportedException();
    public Task<string> GetNameById(int id) => throw new NotSupportedException();
    public IEnumerable<GameClient> GetClientsById(Dictionary<int, MessengerBuddy>.KeyCollection users) => throw new NotSupportedException();
    public void StaffAlert(IServerPacket message, int exclude = 0) => throw new NotSupportedException();
    public void ModAlert(string message) => throw new NotSupportedException();
    public void DoAdvertisingReport(GameClient reporter, GameClient target) => throw new NotSupportedException();
    public void LogClonesOut(int userId) => throw new NotSupportedException();
    public void RegisterClient(GameClient client, int userId, string username) => Online[userId] = client;
    public void UnregisterClient(GameClient? client, int userId, string username) => Online.Remove(userId);
    public void CloseAll() => throw new NotSupportedException();
}
