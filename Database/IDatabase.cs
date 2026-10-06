using System.Data;

namespace Plus.Database;

public interface IDatabase
{
    bool IsConnected();
    IDbConnection Connection();
}