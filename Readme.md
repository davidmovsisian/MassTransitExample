# MassTransit — Transactional Outbox Sample

This sample demonstrates the **Transactional Outbox** pattern using:

| Tech | Role |
|---|---|
| [MassTransit](https://masstransit.io/) | Message-bus abstraction |
| RabbitMQ | Message broker |
| ASP.NET Core 8 | HTTP host for both services |
| PostgreSQL | Relational database (one per service) |
| [Dapper](https://github.com/DapperLib/Dapper) | Lightweight ORM for all DB access |

---

## What is the Transactional Outbox?

Publishing a message and updating a database record must both succeed or both fail.  
Without special handling, a crash between the two leaves data in an inconsistent state.

The **Transactional Outbox** pattern solves this by:

1. Writing the *business record* **and** an *outbox record* in **one database transaction**.
2. A separate **dispatcher** reads the outbox table and publishes messages through the broker.
3. The dispatcher marks each row `processed_at = NOW()` only after a successful publish.

```
POST /orders
     │
     ▼
┌──────────────────────────────┐
│  DB Transaction (ServiceA)   │
│  INSERT orders               │
│  INSERT outbox_messages      │
└──────────────────────────────┘
          │
          │ (background polling every 5 s)
          ▼
  OutboxDispatcher publishes OrderCreated via MassTransit
          │
          ▼
    RabbitMQ exchange
          │
          ▼
  ServiceB — OrderCreatedConsumer
  INSERT received_orders
```

---

## Running the sample

**Prerequisites:** Docker + Docker Compose v2

```bash
git clone https://github.com/davidmovsisian/MassTransitExample.git
cd MassTransitExample
docker compose up --build
```

All five containers start:

| Container | Port | Description |
|---|---|---|
| `rabbitmq` | 5672 / 15672 | Broker + management UI |
| `postgres-a` | 5433 | ServiceA database |
| `postgres-b` | 5434 | ServiceB database |
| `service-a` | 5001 | Producer + Outbox dispatcher |
| `service-b` | 5002 | Consumer |

---

## Triggering the flow

```bash
curl -s -X POST http://localhost:5001/orders \
     -H "Content-Type: application/json" \
     -d '{"customerName":"Alice","amount":99.99}' | jq
```

Expected response:
```json
{
  "id": "...",
  "customerName": "Alice",
  "amount": 99.99,
  "status": "Pending"
}
```

Watch the logs:

```
service-a  | Order <id> created and outbox message <id> inserted in the same transaction.
service-a  | Outbox message <id> (OrderCreated) published successfully.
service-b  | ServiceB consumed OrderCreated for OrderId=<id>, Customer=Alice, Amount=99.99. Saved as ReceivedOrder <id>.
```

---

## Key code snippets

### 1 — Outbox insert (same transaction as business record)

```csharp
// ServiceA/Program.cs
await using var tx = await conn.BeginTransactionAsync();

await conn.ExecuteAsync(
    "INSERT INTO orders (id, customer_name, amount, status, created_at) VALUES (@Id, @CustomerName, @Amount, @Status, @CreatedAt)",
    order, transaction: tx);

await conn.ExecuteAsync(
    "INSERT INTO outbox_messages (id, occurred_at, type, payload) VALUES (@Id, @OccurredAt, @Type, @Payload)",
    outboxMsg, transaction: tx);

await tx.CommitAsync();
```

### 2 — Dispatcher publish (with FOR UPDATE SKIP LOCKED)

```csharp
// ServiceA/Services/OutboxDispatcher.cs
const string selectSql = """
    SELECT id, type, payload, attempts
    FROM   outbox_messages
    WHERE  processed_at IS NULL
    ORDER  BY occurred_at
    LIMIT  10
    FOR UPDATE SKIP LOCKED
    """;

var rows = await conn.QueryAsync(selectSql, transaction: tx);

foreach (var row in rows)
{
    await PublishMessage(row.type, row.payload, ct);
    await conn.ExecuteAsync(
        "UPDATE outbox_messages SET processed_at = NOW(), attempts = @attempts WHERE id = @id",
        new { id = row.id, attempts = row.attempts + 1 }, transaction: tx);
}

await tx.CommitAsync(ct);
```

### 3 — Consumer (ServiceB)

```csharp
// ServiceB/Consumers/OrderCreatedConsumer.cs
public async Task Consume(ConsumeContext<OrderCreated> context)
{
    var msg = context.Message;
    await conn.ExecuteAsync(
        "INSERT INTO received_orders (id, order_id, customer_name, amount, received_at) VALUES (@Id, @OrderId, @CustomerName, @Amount, @ReceivedAt)",
        new ReceivedOrder { Id = Guid.NewGuid(), OrderId = msg.OrderId, ... });

    logger.LogInformation("ServiceB consumed OrderCreated for OrderId={OrderId}...", msg.OrderId, ...);
}
```

---

## Project structure

```
MassTransitExample/
├── docker-compose.yml
├── ServiceA/
│   ├── Contracts/OrderCreated.cs      # Shared message contract
│   ├── Models/                        # Order, OutboxMessage
│   ├── Data/                          # DbConnectionFactory, DatabaseInitializer
│   ├── Services/OutboxDispatcher.cs   # Background polling + publish
│   ├── Program.cs                     # POST /orders endpoint + DI wiring
│   └── Dockerfile
└── ServiceB/
    ├── Contracts/OrderCreated.cs      # Same contract (same namespace)
    ├── Models/ReceivedOrder.cs
    ├── Data/                          # DbConnectionFactory, DatabaseInitializer
    ├── Consumers/OrderCreatedConsumer.cs
    ├── Program.cs
    └── Dockerfile
```
