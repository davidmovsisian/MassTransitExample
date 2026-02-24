using System.Text.Json;
using Dapper;
using MassTransitExample.Contracts;
using Npgsql;
using ServiceA_RMQ.Data;

namespace ServiceA_RMQ.Services;

/// <summary>
/// Background service that polls the outbox_messages table and publishes
/// pending messages through RabbitMQ.  Uses FOR UPDATE SKIP LOCKED to
/// avoid duplicate processing when multiple replicas are running.
/// </summary>
public class OutboxDispatcher(
    DbConnectionFactory db,
    RabbitMqPublisher publisher,
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
                PublishMessage(type, payload);

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

    private void PublishMessage(string type, string payload)
    {
        switch (type)
        {
            case nameof(OrderCreated):
                _ = JsonSerializer.Deserialize<OrderCreated>(payload)
                    ?? throw new InvalidOperationException($"Failed to deserialize payload for message type: {type}");
                publisher.Publish(type, payload);
                break;
            default:
                throw new InvalidOperationException($"Unknown message type: {type}");
        }
    }
}
