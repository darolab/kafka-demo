using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KafkaDemo.DTO;

public class RegistrationRequestDTO : IValidatableObject
{
    [Required]
    [StringLength(64, MinimumLength = 3)]
    [RegularExpression("^[A-Za-z0-9_.-]+$")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;

    [Range(18, 100)]
    public int? Age { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AdditionalProperties?.Keys.Any(key =>
                string.Equals(key, "role", StringComparison.OrdinalIgnoreCase)) == true)
        {
            yield return new ValidationResult(
                "Roles cannot be assigned during registration.",
                [nameof(AdditionalProperties)]);
        }
    }
}

public sealed class LoginRequestDTO
{
    [Required]
    [StringLength(320, MinimumLength = 1)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Password { get; set; } = string.Empty;
}

public sealed class BootstrapRequestDTO : RegistrationRequestDTO
{
}

public sealed class AssignRoleRequestDTO
{
    [Required]
    [RegularExpression("^(Admin|User)$")]
    public string Role { get; set; } = string.Empty;
}

public sealed class AccountStatusRequestDTO
{
    public bool Enabled { get; set; }
}

public sealed class CreateLegacyUserRequestDTO
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [Range(18, 100)]
    public int? Age { get; set; }
}

public sealed class UpdateUserProfileRequestDTO
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [Range(18, 100)]
    public int? Age { get; set; }
}

public sealed class ProvisionCredentialsRequestDTO
{
    [Required]
    [StringLength(64, MinimumLength = 3)]
    [RegularExpression("^[A-Za-z0-9_.-]+$")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;
}
