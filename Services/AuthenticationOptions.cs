using System.ComponentModel.DataAnnotations;

namespace KafkaDemo.Services;

public sealed class AuthenticationOptions : IValidatableObject
{
    public const string SectionName = "Authentication";

    [Range(1, 100)]
    public int MaxFailedLoginAttempts { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 5;

    public string BootstrapSecret { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(BootstrapSecret) && BootstrapSecret.Length < 32)
        {
            yield return new ValidationResult(
                "BootstrapSecret must contain at least 32 characters when configured.",
                [nameof(BootstrapSecret)]);
        }
    }
}
