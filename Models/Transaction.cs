namespace KafkaDemo.Models;

public class Transaction
{
    public long Id {get; set;}
    public string TransactionNumber {get; set; } = string.Empty;
    public string FromAccount{get; set;} = string.Empty;

    public string ToAccount { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";

    public string Status { get; set; } = "SUCCESS";

    public DateTime CreatedAt { get; set; }
}