

namespace KafkaDemo.Models;
using System.ComponentModel.DataAnnotations;


public class User
{
    public int Id { get; set; }

    [Range(18, 100)]
    public string Name { get; set; } = string.Empty;


    [Required]
    public string Email { get; set; } = string.Empty;

    public int? Age { get; set; }

    public DateTime CreatedAt { get; set; }
}