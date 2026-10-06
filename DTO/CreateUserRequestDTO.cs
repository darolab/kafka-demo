using System.ComponentModel.DataAnnotations;
namespace KafkaDemo.DTO;

public class CreateUserRequestDTO
{
    [Required]
    public string FullName { get; set; }

    public string Email { get; set; }

    [Range(18, 100)]
    public int Age { get; set; }
}