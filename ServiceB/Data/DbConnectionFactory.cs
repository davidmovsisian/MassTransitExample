using Npgsql;
using System.Data;

namespace ServiceB.Data;

public class DbConnectionFactory(string connectionString)
{
    public IDbConnection CreateConnection() => new NpgsqlConnection(connectionString);
}
