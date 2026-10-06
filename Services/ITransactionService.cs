using KafkaDemo.DTO;
using KafkaDemo.Models;

namespace KafkaDemo.Services;

public interface ITransactionService
{
    Task<Transaction> CreateAsync(
        CreateTransactionRequest request,
        CancellationToken cancellationToken);
}