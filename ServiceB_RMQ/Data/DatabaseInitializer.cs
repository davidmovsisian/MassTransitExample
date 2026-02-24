using Dapper;
using Npgsql;

namespace ServiceB_RMQ.Data;

public class DatabaseInitializer(string connectionString, ILogger<DatabaseInitializer> logger)
    : IHostedService
{
    private const string Sql = """
        CREATE TABLE IF NOT EXISTS received_orders (
            id            UUID        PRIMARY KEY,
            order_id      UUID        NOT NULL,
            customer_name TEXT        NOT NULL,
            amount        NUMERIC(18,2) NOT NULL,
            received_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(cancellationToken);
                await conn.ExecuteAsync(Sql);
                logger.LogInformation("ServiceB_RMQ database schema initialized.");
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning("DB not ready (attempt {Attempt}): {Message}. Retrying in 3s…", attempt, ex.Message);
                await Task.Delay(3_000, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
