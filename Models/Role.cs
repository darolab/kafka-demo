using System.ComponentModel.DataAnnotations;

namespace KafkaDemo.Models;

public sealed class Role
{
    public int Id { get; set; }

    [Required]
    [MaxLength(32)]
    public string Name { get; set; } = string.Empty;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
