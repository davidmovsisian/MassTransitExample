using Dapper;
using Npgsql;

namespace ServiceA_RMQ.Data;

public class DatabaseInitializer(string connectionString, ILogger<DatabaseInitializer> logger)
    : IHostedService
{
    private const string Sql = """
        CREATE TABLE IF NOT EXISTS orders (
            id          UUID        PRIMARY KEY,
            customer_name TEXT      NOT NULL,
            amount      NUMERIC(18,2) NOT NULL,
            status      TEXT        NOT NULL DEFAULT 'Pending',
            created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS outbox_messages (
            id           UUID        PRIMARY KEY,
            occurred_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            type         TEXT        NOT NULL,
            payload      TEXT        NOT NULL,
            processed_at TIMESTAMPTZ,
            error        TEXT,
            attempts     INT         NOT NULL DEFAULT 0
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
                logger.LogInformation("ServiceA_RMQ database schema initialized.");
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
