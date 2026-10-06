using System.Data;
using Dapper;
using MySqlConnector;

namespace Plus.Database;

internal sealed class UtcDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override DateTimeOffset Parse(object value) => value switch
    {
        DateTimeOffset offset => offset.ToUniversalTime(),
        MySqlDateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime.GetDateTime(), DateTimeKind.Utc)),
        DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        _ => throw new DataException($"Cannot map {value.GetType().Name} to DateTimeOffset.")
    };

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        parameter.DbType = DbType.DateTime;
        parameter.Value = value.UtcDateTime;
    }
}
