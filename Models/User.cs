

namespace KafkaDemo.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;


public class User
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;


    [Required]
    public string Email { get; set; } = string.Empty;

    [MaxLength(320)]
    [JsonIgnore]
    public string NormalizedEmail { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? Username { get; set; }

    [MaxLength(64)]
    [JsonIgnore]
    public string? NormalizedUsername { get; set; }

    [MaxLength(512)]
    [JsonIgnore]
    public string? PasswordHash { get; set; }

    [JsonIgnore]
    public bool IsEnabled { get; set; }

    [JsonIgnore]
    public int FailedLoginCount { get; set; }

    [JsonIgnore]
    public DateTime? LockoutUntilUtc { get; set; }

    public int? Age { get; set; }

    public DateTime CreatedAt { get; set; }

    [JsonIgnore]
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}