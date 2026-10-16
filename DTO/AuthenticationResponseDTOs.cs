namespace KafkaDemo.DTO;

public sealed record LoginResponseDTO(string Token, DateTime ExpiresAtUtc);
