namespace ServiceB.Models;

public class ReceivedOrder
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime ReceivedAt { get; set; }
}
