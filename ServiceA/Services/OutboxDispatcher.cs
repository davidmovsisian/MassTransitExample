using System.Text.Json;
using Dapper;
using MassTransit;
using MassTransitExample.Contracts;
using Npgsql;
using ServiceA.Data;

namespace ServiceA.Services;

/// <summary>
/// Background service that polls the outbox_messages table and publishes
/// pending messages through MassTransit.  Uses FOR UPDATE SKIP LOCKED to
/// avoid duplicate processing when multiple replicas are running.
/// </summary>
public class OutboxDispatcher(
    DbConnectionFactory db,
    IBus bus,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxDispatcher started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessages(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "OutboxDispatcher encountered an error.");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingMessages(CancellationToken ct)
    {
        await using var conn = (NpgsqlConnection)db.CreateConnection();
        await conn.OpenAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(ct);

        // Lock up to 10 unprocessed rows; skip any already locked by another replica
        const string selectSql = """
            SELECT id, type, payload, attempts
            FROM   outbox_messages
            WHERE  processed_at IS NULL
            ORDER  BY occurred_at
            LIMIT  10
            FOR UPDATE SKIP LOCKED
            """;

        var rows = (await conn.QueryAsync(selectSql, transaction: tx)).AsList();

        if (rows.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return;
        }

        foreach (var row in rows)
        {
            Guid id = row.id;
            string type = row.type;
            string payload = row.payload;
            int attempts = row.attempts;

            try
            {
                await PublishMessage(type, payload, ct);

                await conn.ExecuteAsync(
                    "UPDATE outbox_messages SET processed_at = NOW(), attempts = @attempts WHERE id = @id",
                    new { id, attempts = attempts + 1 }, transaction: tx);

                logger.LogInformation("Outbox message {Id} ({Type}) published successfully.", id, type);
            }
            catch (Exception ex)
            {
                await conn.ExecuteAsync(
                    "UPDATE outbox_messages SET attempts = @attempts, error = @error WHERE id = @id",
                    new { id, attempts = attempts + 1, error = ex.Message }, transaction: tx);

                logger.LogError(ex, "Failed to publish outbox message {Id}.", id);
            }
        }

        await tx.CommitAsync(ct);
    }

    private Task PublishMessage(string type, string payload, CancellationToken ct)
    {
        return type switch
        {
            nameof(OrderCreated) => bus.Publish(
                JsonSerializer.Deserialize<OrderCreated>(payload)!, ct),
            _ => throw new InvalidOperationException($"Unknown message type: {type}")
        };
    }
}
