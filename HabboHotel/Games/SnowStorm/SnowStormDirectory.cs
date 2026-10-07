using Microsoft.Extensions.Logging;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Game;
using Plus.Communication.Packets.Outgoing.Game.SnowStorm;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Games.SnowStorm;

/// <summary>The games_main window data: directory and account status, leaderboards and game-token offers.</summary>
public interface ISnowStormDirectory
{
    void ShowDirectoryStatus(GameClient session);

    void ShowAccountStatus(GameClient session, int gameTypeId);

    void ShowLeaderboard(GameClient session, SnowStormLeaderboardKind kind, int gameTypeId, int weekOffset, int startRank, int viewSize, int windowSize);

    void ShowTokenOffers(GameClient session);

    void PurchaseTokens(GameClient session, int offerId);
}

public sealed class SnowStormDirectory(
    ISnowStormStore store,
    ISnowStormManager manager,
    ISettingsManager settings,
    TimeProvider clock,
    ILogger<SnowStormDirectory> logger) : ISnowStormDirectory
{
    public const int GameTypeId = 0;

    // AIR Game2GameDirectoryStatus: 0 = available, anything else keeps games_main closed.
    private const int Available = 0;
    private const int Unavailable = 2;

    public void ShowDirectoryStatus(GameClient session)
    {
        var config = SnowStormSettings.Read(settings);

        if (!config.Enabled) {
            session.Send(new Game2GameDirectoryStatusComposer(Unavailable, 0, 0, 0));

            return;
        }

        var userId = session.GetHabbo().Id;
        var account = Account(userId);
        session.Send(new Game2GameDirectoryStatusComposer(Available, manager.BlockSeconds(userId), account?.GamesPlayed ?? 0,
            account?.GamesLeft(config.FreeGamesPerDay) ?? 0));
    }

    public void ShowAccountStatus(GameClient session, int gameTypeId)
    {
        if (gameTypeId != GameTypeId) {
            session.Send(new GameAccountStatusComposer(gameTypeId));

            return;
        }

        var config = SnowStormSettings.Read(settings);
        var account = Account(session.GetHabbo().Id);
        session.Send(new GameAccountStatusComposer(GameTypeId, account?.GamesLeft(config.FreeGamesPerDay) ?? 0, account?.GamesPlayed ?? 0));
    }

    public void ShowLeaderboard(GameClient session, SnowStormLeaderboardKind kind, int gameTypeId, int weekOffset, int startRank, int viewSize, int windowSize)
    {
        if (gameTypeId != GameTypeId) {
            return;
        }

        SnowStormLeaderboardPage page;

        try {
            page = store.LoadLeaderboard(new(kind, session.GetHabbo().Id, weekOffset, startRank, viewSize, windowSize), clock.GetUtcNow());
        }
        catch (Exception exception) {
            logger.LogError(exception, "Unable to load the SnowStorm {Kind} leaderboard", kind);

            return;
        }

        session.Send(kind switch
        {
            SnowStormLeaderboardKind.Total => new Game2TotalLeaderboardComposer(page),
            SnowStormLeaderboardKind.Friends => new Game2FriendsLeaderboardComposer(page),
            SnowStormLeaderboardKind.Weekly => new Game2WeeklyLeaderboardComposer(page),
            SnowStormLeaderboardKind.WeeklyFriends => new Game2WeeklyFriendsLeaderboardComposer(page),
            SnowStormLeaderboardKind.TotalGroup => new Game2TotalGroupLeaderboardComposer(page),
            _ => (IServerPacket)new Game2WeeklyGroupLeaderboardComposer(page)
        });
    }

    public void ShowTokenOffers(GameClient session)
    {
        try {
            session.Send(new SnowWarGameTokensComposer(store.GetOffers()));
        }
        catch (Exception exception) {
            logger.LogError(exception, "Unable to load the SnowStorm game-token offers");
        }
    }

    public void PurchaseTokens(GameClient session, int offerId)
    {
        var habbo = session.GetHabbo();
        SnowStormTokenOffer? offer;

        try {
            offer = store.Purchase(habbo, offerId);
        }
        catch (Exception exception) {
            logger.LogError(exception, "Unable to sell SnowStorm game-token offer {OfferId} to {UserId}", offerId, habbo.Id);
            offer = null;
        }

        if (offer == null) {
            session.Send(new PurchaseErrorComposer(PurchaseError.Unavailable));

            return;
        }

        session.Send(new CreditBalanceComposer(habbo.Credits));

        if (offer.PricePoints > 0) {
            var balance = offer.PointsType == 5 ? habbo.Diamonds : habbo.Duckets;
            session.Send(new HabboActivityPointNotificationComposer(balance, -offer.PricePoints, offer.PointsType));
        }

        session.Send(new PurchaseOKComposer());
        ShowAccountStatus(session, GameTypeId);
    }

    private SnowStormAccount? Account(int userId)
    {
        try {
            return store.GetAccount(userId, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        }
        catch (Exception exception) {
            logger.LogError(exception, "Unable to load the SnowStorm account of {UserId}", userId);

            return null;
        }
    }
}
