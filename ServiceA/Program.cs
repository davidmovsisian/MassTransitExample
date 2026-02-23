using System.Text.Json;
using Dapper;
using MassTransit;
using MassTransitExample.Contracts;
using Npgsql;
using ServiceA.Data;
using ServiceA.Models;
using ServiceA.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("ServiceA")
              ?? throw new InvalidOperationException("Connection string 'ServiceA' is missing.");

var dbFactory = new DbConnectionFactory(connStr);
builder.Services.AddSingleton(dbFactory);
builder.Services.AddHostedService(sp =>
    new DatabaseInitializer(connStr, sp.GetRequiredService<ILogger<DatabaseInitializer>>()));

// ── MassTransit / RabbitMQ ────────────────────────────────────────────────────
var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
    });
});

// ── Outbox Dispatcher (background service) ────────────────────────────────────
builder.Services.AddHostedService<OutboxDispatcher>();

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

// ── POST /orders ──────────────────────────────────────────────────────────────
app.MapPost("/orders", async (CreateOrderRequest req, DbConnectionFactory factory, ILogger<Program> logger) =>
{
    var order = new Order
    {
        Id = Guid.NewGuid(),
        CustomerName = req.CustomerName,
        Amount = req.Amount,
        Status = "Pending",
        CreatedAt = DateTime.UtcNow
    };

    var outboxMsg = new OutboxMessage
    {
        Id = Guid.NewGuid(),
        OccurredAt = order.CreatedAt,
        Type = nameof(OrderCreated),
        Payload = JsonSerializer.Serialize(new OrderCreated(order.Id, order.CustomerName, order.Amount, order.CreatedAt))
    };

    await using var conn = (NpgsqlConnection)factory.CreateConnection();
    await conn.OpenAsync();
    await using var tx = await conn.BeginTransactionAsync();

    await conn.ExecuteAsync(
        "INSERT INTO orders (id, customer_name, amount, status, created_at) VALUES (@Id, @CustomerName, @Amount, @Status, @CreatedAt)",
        order, transaction: tx);

    await conn.ExecuteAsync(
        "INSERT INTO outbox_messages (id, occurred_at, type, payload) VALUES (@Id, @OccurredAt, @Type, @Payload)",
        outboxMsg, transaction: tx);

    await tx.CommitAsync();

    logger.LogInformation(
        "Order {OrderId} created and outbox message {OutboxId} inserted in the same transaction.",
        order.Id, outboxMsg.Id);

    return Results.Created($"/orders/{order.Id}", new { order.Id, order.CustomerName, order.Amount, order.Status });
});

app.Run();

record CreateOrderRequest(string CustomerName, decimal Amount);
