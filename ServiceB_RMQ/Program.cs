using ServiceB_RMQ.Consumers;
using ServiceB_RMQ.Data;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("ServiceB")
              ?? throw new InvalidOperationException("Connection string 'ServiceB' is missing.");

var dbFactory = new DbConnectionFactory(connStr);
builder.Services.AddSingleton(dbFactory);
builder.Services.AddHostedService(sp =>
    new DatabaseInitializer(connStr, sp.GetRequiredService<ILogger<DatabaseInitializer>>()));

// ── RabbitMQ consumer ─────────────────────────────────────────────────────────
var rabbitOptions = builder.Configuration.GetSection("RabbitMq").Get<RabbitMqOptions>()
                   ?? new RabbitMqOptions();
rabbitOptions.Host = builder.Configuration["RabbitMq:Host"] ?? rabbitOptions.Host;

builder.Services.AddSingleton(rabbitOptions);
builder.Services.AddHostedService<OrderCreatedConsumerService>();

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
