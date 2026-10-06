using System.Data;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Tests;

internal sealed class TestNavigationDatabase : IDatabase
{
    public static TestNavigationDatabase Instance { get; } = new();

    public bool IsConnected() => throw Unused();
    public IDbConnection Connection() => throw Unused();

    private static InvalidOperationException Unused() =>
        new("Unused navigation persistence must remain lazy.");
}

internal sealed class TestNavigationRewards : IRewardTrackManager
{
    public static TestNavigationRewards Instance { get; } = new();

    public void Progress(GameClient session, string actionType, int amount = 1) => throw Unused();
    public void SendTracks(GameClient session) => throw Unused();
    public Task Claim(GameClient session, string trackId, string prizeId) => throw Unused();
    public void PurchasePremium(GameClient session, string trackId) => throw Unused();

    private static InvalidOperationException Unused() =>
        new("Unused navigation rewards must remain lazy.");
}
