using KafkaDemo;
using Microsoft.AspNetCore.Mvc;
using KafkaDemo.DTO;

namespace KafkaDemo.Controllers;

[ApiController]
[Route("api/messages")]
public class MessageController : ControllerBase
{
    private readonly KafkaProducer _producer;

    public MessageController(KafkaProducer producer)
    {
        _producer = producer;
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage(MessageDTO messageDTO)
    {
        await _producer.SendMessage(messageDTO.message);

        return Ok(new
        {
            message = "Message sent to Kafka",
            data = messageDTO.message
        });
    }
}