using System.Text;
using RabbitMQ.Client;

namespace ServiceA_RMQ.Services;

/// <summary>
/// Manages a single RabbitMQ connection and channel for publishing messages.
/// Automatically reconnects on transient failures.
/// </summary>
public sealed class RabbitMqPublisher : IDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly object _lock = new();

    public const string ExchangeName = "order.created";

    public RabbitMqPublisher(RabbitMqOptions options, ILogger<RabbitMqPublisher> logger)
    {
        _logger = logger;
        _factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.Username,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };
    }

    /// <summary>
    /// Publishes a message body (UTF-8 JSON) to the fanout exchange.
    /// Re-establishes the channel if it is closed.
    /// </summary>
    public void Publish(string messageType, string jsonPayload)
    {
        lock (_lock)
        {
            var channel = GetOrCreateChannelLocked();
            var body = Encoding.UTF8.GetBytes(jsonPayload);

            var props = channel.CreateBasicProperties();
            props.ContentType = "application/json";
            props.DeliveryMode = 2; // persistent
            props.Type = messageType;

            channel.BasicPublish(
                exchange: ExchangeName,
                routingKey: string.Empty,
                basicProperties: props,
                body: body);
        }
    }

    private IModel GetOrCreateChannelLocked()
    {
        if (_channel is { IsOpen: true })
            return _channel;

        _channel?.Dispose();
        _connection?.Dispose();

        _logger.LogInformation("RabbitMqPublisher: (re)connecting to RabbitMQ…");
        _connection = _factory.CreateConnection();
        _channel = _connection.CreateModel();

        // Declare durable fanout exchange so ServiceB_RMQ can bind a queue to it
        _channel.ExchangeDeclare(
            exchange: ExchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false);

        _logger.LogInformation("RabbitMqPublisher: connected and exchange '{Exchange}' declared.", ExchangeName);
        return _channel;
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
