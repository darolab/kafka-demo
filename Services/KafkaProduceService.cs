using Confluent.Kafka;
namespace KafkaDemo.Services;


public class KafkaProducerService : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KafkaProducerService> _logger;

    public KafkaProducerService(
        IConfiguration configuration,
        ILogger<KafkaProducerService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var bootstrapServers =
            _configuration["Kafka:BootstrapServers"];

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,

            ClientId = "transaction-api",

            Acks = Acks.All,

            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string, string>(config)
            .Build();
    }

    public async Task ProduceAsync(
        string topic,
        string key,
        string message,
        CancellationToken cancellationToken = default)
    {
        var kafkaMessage = new Message<string, string>
        {
            Key = key,
            Value = message
        };

        var result = await _producer.ProduceAsync(
            topic,
            kafkaMessage,
            cancellationToken);

        _logger.LogInformation(
            "Kafka message produced. Topic: {Topic}, Partition: {Partition}, Offset: {Offset}",
            result.Topic,
            result.Partition,
            result.Offset);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(10));
        _producer.Dispose();
    }
}