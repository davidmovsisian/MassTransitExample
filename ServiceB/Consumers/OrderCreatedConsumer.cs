using Dapper;
using MassTransit;
using MassTransitExample.Contracts;
using Npgsql;
using ServiceB.Data;
using ServiceB.Models;

namespace ServiceB.Consumers;

public class OrderCreatedConsumer(DbConnectionFactory db, ILogger<OrderCreatedConsumer> logger)
    : IConsumer<OrderCreated>
{
    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        var msg = context.Message;

        var received = new ReceivedOrder
        {
            Id = Guid.NewGuid(),
            OrderId = msg.OrderId,
            CustomerName = msg.CustomerName,
            Amount = msg.Amount,
            ReceivedAt = DateTime.UtcNow
        };

        await using var conn = (NpgsqlConnection)db.CreateConnection();
        await conn.OpenAsync(context.CancellationToken);

        await conn.ExecuteAsync(
            "INSERT INTO received_orders (id, order_id, customer_name, amount, received_at) VALUES (@Id, @OrderId, @CustomerName, @Amount, @ReceivedAt)",
            received);

        logger.LogInformation(
            "ServiceB consumed OrderCreated for OrderId={OrderId}, Customer={CustomerName}, Amount={Amount}. Saved as ReceivedOrder {Id}.",
            msg.OrderId, msg.CustomerName, msg.Amount, received.Id);
    }
}
