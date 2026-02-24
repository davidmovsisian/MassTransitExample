using System.Text;
using System.Text.Json;
using Dapper;
using MassTransitExample.Contracts;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ServiceB_RMQ.Data;
using ServiceB_RMQ.Models;

namespace ServiceB_RMQ.Consumers;

/// <summary>
/// Background service that consumes OrderCreated messages from RabbitMQ.
/// Automatically reconnects on transient failures.
/// </summary>
public class OrderCreatedConsumerService(
    RabbitMqOptions options,
    DbConnectionFactory db,
    ILogger<OrderCreatedConsumerService> logger) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OrderCreatedConsumerService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RabbitMQ consumer disconnected. Reconnecting in {Delay}s…", ReconnectDelay.TotalSeconds);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }

        logger.LogInformation("OrderCreatedConsumerService stopped.");
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.Username,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            DispatchConsumersAsync = false
        };

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        // Declare the fanout exchange (idempotent – safe to call on every connect)
        channel.ExchangeDeclare(
            exchange: options.ExchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false);

        // Declare and bind a durable queue
        channel.QueueDeclare(
            queue: options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);

        channel.QueueBind(
            queue: options.QueueName,
            exchange: options.ExchangeName,
            routingKey: string.Empty);

        // Process one message at a time so we don't lose messages on failure
        channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.Span);
                var msg = JsonSerializer.Deserialize<OrderCreated>(json)
                          ?? throw new InvalidOperationException("Failed to deserialize OrderCreated message.");

                await HandleOrderCreated(msg, stoppingToken);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing message. Message will be requeued.");
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        connection.ConnectionShutdown += (_, _) => tcs.TrySetException(new Exception("Connection shutdown."));
        channel.ModelShutdown += (_, _) => tcs.TrySetException(new Exception("Channel shutdown."));

        channel.BasicConsume(queue: options.QueueName, autoAck: false, consumer: consumer);
        logger.LogInformation("Consuming from queue '{Queue}' on exchange '{Exchange}'.", options.QueueName, options.ExchangeName);

        // Wait until either the token is cancelled or the connection drops
        using var reg = stoppingToken.Register(() => tcs.TrySetCanceled(stoppingToken));
        await tcs.Task;
    }

    private async Task HandleOrderCreated(OrderCreated msg, CancellationToken ct)
    {
        var received = new ReceivedOrder
        {
            Id = Guid.NewGuid(),
            OrderId = msg.OrderId,
            CustomerName = msg.CustomerName,
            Amount = msg.Amount,
            ReceivedAt = DateTime.UtcNow
        };

        await using var conn = (NpgsqlConnection)db.CreateConnection();
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(
            "INSERT INTO received_orders (id, order_id, customer_name, amount, received_at) VALUES (@Id, @OrderId, @CustomerName, @Amount, @ReceivedAt)",
            received);

        logger.LogInformation(
            "ServiceB_RMQ consumed OrderCreated for OrderId={OrderId}, Customer={CustomerName}, Amount={Amount}. Saved as ReceivedOrder {Id}.",
            msg.OrderId, msg.CustomerName, msg.Amount, received.Id);
    }
}
