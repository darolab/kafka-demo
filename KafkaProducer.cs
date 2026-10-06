using Confluent.Kafka;
namespace KafkaDemo;


public class KafkaProducer
{
    private readonly string _bootstrapServers = "localhost:9092";
    private readonly string _topic = "trx-message";

    public async Task SendMessage(string message)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = _bootstrapServers
        };

        using var producer = new ProducerBuilder<Null, string>(config).Build();

        var result = await producer.ProduceAsync(
            _topic,
            new Message<Null, string>
            {
                Value = message
            });

        Console.WriteLine(
            $"Message sent to {result.Topic}, partition {result.Partition}, offset {result.Offset}");
    }
}