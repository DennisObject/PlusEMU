using Dapper;

namespace Plus.HabboHotel.Users.Calendar;

/// <summary>
/// Permissions for a specific Player.
/// </summary>
public sealed class CalendarComponent
{
    /// <summary>
    /// Permission rights are stored here.
    /// </summary>
    private readonly List<int> _lateBoxes;

    private readonly List<int> _openedBoxes;

    public CalendarComponent()
    {
        _lateBoxes = new();
        _openedBoxes = new();
    }

    /// <summary>
    /// Initialize the PermissionComponent.
    /// </summary>
    /// <param name="player"></param>
    public bool Init(Habbo player)
    {
        if (_lateBoxes.Count > 0)
            _lateBoxes.Clear();
        if (_openedBoxes.Count > 0)
            _openedBoxes.Clear();
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        foreach (var row in connection.Query<CalendarEntry>("SELECT `day`, `status` FROM `user_xmas15_calendar` WHERE `user_id` = @id", new { id = player.Id }))
        {
            if (row.Status == 0) _lateBoxes.Add(row.Day);
            else _openedBoxes.Add(row.Day);
        }
        return true;
    }

    private sealed record CalendarEntry(int Day, int Status);

    public List<int> GetOpenedBoxes() => _openedBoxes;

    public List<int> GetLateBoxes() => _lateBoxes;

    /// <summary>
    /// Dispose of the permissions list.
    /// </summary>
    public void Dispose()
    {
        _lateBoxes.Clear();
        _openedBoxes.Clear();
    }
}
