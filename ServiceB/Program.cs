using MassTransit;
using ServiceB.Consumers;
using ServiceB.Data;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("ServiceB")
              ?? throw new InvalidOperationException("Connection string 'ServiceB' is missing.");

var dbFactory = new DbConnectionFactory(connStr);
builder.Services.AddSingleton(dbFactory);
builder.Services.AddHostedService(sp =>
    new DatabaseInitializer(connStr, sp.GetRequiredService<ILogger<DatabaseInitializer>>()));

// ── MassTransit / RabbitMQ ────────────────────────────────────────────────────
var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.ConfigureEndpoints(ctx);
    });
});

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
