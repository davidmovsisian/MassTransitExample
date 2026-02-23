namespace MassTransitExample.Contracts;

public record OrderCreated(Guid OrderId, string CustomerName, decimal Amount, DateTime OccurredAt);
