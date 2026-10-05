using Microsoft.Extensions.Logging;
using Plus.Core.Settings;
using Plus.HabboHotel.Achievements;

namespace Plus.HabboHotel.Users.Process;

public interface IUserProcessFactory
{
    ProcessComponent Create();
}

public sealed class UserProcessFactory(ILogger<ProcessComponent> logger, TimeProvider clock,
    IUserProcessStore store, IAchievementManager achievements, ISettingsManager settings) : IUserProcessFactory
{
    public ProcessComponent Create() => new(logger, clock, store, achievements, settings);
}
