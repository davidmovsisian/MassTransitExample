using Npgsql;
using System.Data;

namespace ServiceA.Data;

public class DbConnectionFactory(string connectionString)
{
    public IDbConnection CreateConnection() => new NpgsqlConnection(connectionString);
}
