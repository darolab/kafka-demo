namespace KafkaDemo.Models;

public sealed class AuthenticationBootstrapState
{
    public int Id { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
