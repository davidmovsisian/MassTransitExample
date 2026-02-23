namespace ServiceA.Models;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime? ProcessedAt { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
}
