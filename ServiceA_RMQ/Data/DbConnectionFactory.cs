using Npgsql;
using System.Data;

namespace ServiceA_RMQ.Data;

public class DbConnectionFactory(string connectionString)
{
    public IDbConnection CreateConnection() => new NpgsqlConnection(connectionString);
}
