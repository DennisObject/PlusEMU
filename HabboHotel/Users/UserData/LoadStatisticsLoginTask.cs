using Microsoft.Extensions.Logging;
using Plus.HabboHotel.Groups;

namespace Plus.HabboHotel.Users.UserData;

internal class LoadStatisticsLoginTask : IUserDataLoadingTask
{
    private readonly IHabboStatsService _habboStatsService;
    private readonly IGroupManager _groups;
    private readonly TimeProvider _clock;
    private readonly ILogger<LoadStatisticsLoginTask> _logger;

    public LoadStatisticsLoginTask(IHabboStatsService habboStatsService, IGroupManager groups,
        TimeProvider clock, ILogger<LoadStatisticsLoginTask> logger)
    {
        _habboStatsService = habboStatsService;
        _groups = groups;
        _clock = clock;
        _logger = logger;
    }

    public async Task Load(Habbo habbo)
    {
        if (habbo == null) {
            throw new ArgumentNullException(nameof(habbo));
        }

        try {
            var stats = await _habboStatsService.LoadHabboStats(habbo.Id);

            var day = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone).ToString("MM/dd");

            if (stats.RespectsTimestamp != day) {
                var dailyRespects = 10;
                stats.DailyRespectPoints = dailyRespects;
                stats.DailyPetRespectPoints = dailyRespects;
                stats.RespectsTimestamp = day;

                await _habboStatsService.UpdateDailyRespectsAndTimestamp(habbo.Id, dailyRespects, stats.RespectsTimestamp);
            }

            if (!_groups.TryGetGroup(stats.FavouriteGroupId, out var group)) {
                stats.FavouriteGroupId = 0;
            }

            habbo.HabboStats = stats;
        }
        catch (Exception e) {
            _logger.LogError(e, "Failed to load statistics for {UserId}", habbo.Id);
        }
    }
}
