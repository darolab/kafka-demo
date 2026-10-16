using System.ComponentModel.DataAnnotations;

namespace KafkaDemo.Services;

public sealed class JwtOptions
{
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int TokenLifetimeMinutes { get; set; } = 60;
}