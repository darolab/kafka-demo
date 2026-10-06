using System.ComponentModel.DataAnnotations;

namespace KafkaDemo.DTO;

public class CreateTransactionRequest
{
    [Required]
    public string FromAccount { get; set; } = string.Empty;

    [Required]
    public string ToAccount { get; set; } = string.Empty;

    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";
}