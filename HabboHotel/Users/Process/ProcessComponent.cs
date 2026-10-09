using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.Core.Settings;
using Plus.HabboHotel.Achievements;

namespace Plus.HabboHotel.Users.Process;

public sealed class ProcessComponent(ILogger<ProcessComponent> logger, TimeProvider clock,
    IUserProcessStore store, IAchievementManager achievements, ISettingsManager settings) : IDisposable
{
    private readonly object _timerGate = new();
    private Habbo? _player;
    private ITimer? _timer;
    private int _running;
    private bool _disposed;

    public bool Init(Habbo player)
    {
        if (player == null) {
            return false;
        }

        lock (_timerGate) {
            if (_disposed || _player != null) {
                return false;
            }

            _player = player;
            _timer = clock.CreateTimer(Run, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            return true;
        }
    }

    public void Run(object? state)
    {
        Habbo player;

        lock (_timerGate) {
            if (_disposed || _player == null) {
                return;
            }

            player = _player;

            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) {
                logger.LogWarning("<Player {PlayerId}> Server can't keep up, Player timer is lagging behind.", player.Id);

                return;
            }
        }

        try {
            var now = clock.GetUtcNow();
            var day = TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone).ToString("MM/dd");

            if (player.TimeMuted > 0) {
                player.TimeMuted -= 60;
            }

            if (player.MessengerSpamTime > 0) {
                player.MessengerSpamTime -= 60;
            }

            if (player.MessengerSpamTime <= 0) {
                player.MessengerSpamCount = 0;
            }

            player.TimeAfk += 1;

            // Keep the reset and its live counts atomic with the final logout save.
            lock (player.WalletSync) {
                if (!player.WalletClosed && player.HabboStats is { } stats && stats.RespectsTimestamp != day) {
                    var respects = player.Access.Limit("limit.daily_respects", 10);
                    var petRespects = player.Access.Limit("limit.daily_pet_respects", 10);
                    store.ResetDailyRespects(player.Id, respects, petRespects, day);
                    stats.RespectsTimestamp = day;
                    stats.DailyRespectPoints = respects;
                    stats.DailyPetRespectPoints = petRespects;

                    if (player.Client != null) {
                        player.Client.Send(new UserObjectComposer(UserObjectSnapshot.Capture(player)));
                    }
                }
            }

            if (player.GiftPurchasingWarnings < 15) {
                player.GiftPurchasingWarnings = 0;
            }

            if (player.MottoUpdateWarnings < 15) {
                player.MottoUpdateWarnings = 0;
            }

            if (player.ClothingUpdateWarnings < 15) {
                player.ClothingUpdateWarnings = 0;
            }

            if (player.Client != null) {
                achievements.ProgressAchievement(player.Client, "ACH_AllTimeHotelPresence", 1);
            }

            player.CheckCreditsTimer(settings);

            if (player.Effects is { } effects) {
                effects.CheckEffectExpiryAt(player, now);
            }
        }
        catch (Exception exception) {
            logger.LogError(exception, "Player process failed for {PlayerId}", player.Id);
        }
        finally {
            Volatile.Write(ref _running, 0);
        }
    }

    public void Dispose()
    {
        ITimer? timer;

        lock (_timerGate) {
            if (_disposed) {
                return;
            }

            _disposed = true;
            _player = null;
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
    }
}
