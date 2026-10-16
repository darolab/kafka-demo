namespace KafkaDemo.DTO;

public sealed record UserResponseDTO(
    int Id,
    string Username,
    string Email,
    string FullName,
    int? Age,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles);
