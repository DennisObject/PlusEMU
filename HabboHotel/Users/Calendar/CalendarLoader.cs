using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users.Calendar;

public interface ICalendarLoader { IReadOnlyList<CalendarEntry> Load(int userId); }
public sealed record CalendarEntry(int Day, int Status);

public sealed class CalendarLoader(IDatabase database) : ICalendarLoader
{
    public IReadOnlyList<CalendarEntry> Load(int userId)
    {
        using var connection = database.Connection();
        return connection.Query<CalendarEntry>("SELECT `day`, `status` FROM `user_xmas15_calendar` WHERE `user_id` = @userId", new { userId }).ToList();
    }
}
