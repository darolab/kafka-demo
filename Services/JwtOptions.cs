using System.ComponentModel.DataAnnotations;

namespace KafkaDemo.Services;

public sealed class JwtOptions
{
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;
}