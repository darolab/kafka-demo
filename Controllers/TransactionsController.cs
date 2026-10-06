using Microsoft.AspNetCore.Mvc;
using KafkaDemo.DTO;
using KafkaDemo.Services;

namespace KafkaDemo.Controllers;
[ApiController]
[Route("api/[controller]")]
public class TransactionsController: ControllerBase
{
    private readonly ITransactionService _transactionService;

    public TransactionsController(
        ITransactionService transactionService)
    {
        _transactionService = transactionService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _transactionService.CreateAsync(
                request,
                cancellationToken);

        return Ok(new
        {
            message = "Transaction created successfully",

            data = result
        });
    }
}