using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using KafkaDemo.Data;
using KafkaDemo.DTO;
using KafkaDemo.Models;
using KafkaDemo.Services;


public class TransactionService : ITransactionService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(
        AppDbContext db,
        ILogger<TransactionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Transaction> CreateAsync(
        CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var trx = new Transaction
            {
                TransactionNumber =
                    $"TXN-{DateTime.UtcNow:yyyyMMddHHmmssfff}",

                FromAccount = request.FromAccount,

                ToAccount = request.ToAccount,

                Amount = request.Amount,

                Currency = request.Currency.ToUpper(),

                Status = "SUCCESS",

                CreatedAt = DateTime.UtcNow
            };

            _db.Transactions.Add(trx);

            await _db.SaveChangesAsync(cancellationToken);

            var eventMessage = new
            {
                eventType = "TransactionCreated",

                transactionId = trx.Id,

                transactionNumber =
                    trx.TransactionNumber,

                fromAccount =
                    trx.FromAccount,

                toAccount =
                    trx.ToAccount,

                amount =
                    trx.Amount,

                currency =
                    trx.Currency,

                status =
                    trx.Status,

                createdAt =
                    trx.CreatedAt
            };

            var payload =
                JsonSerializer.Serialize(eventMessage);

            var outboxMessage = new OutboxMessage
            {
                EventType = "TransactionCreated",

                Topic = "transaction-created",

                Payload = payload,

                CreatedAt = DateTime.UtcNow,

                PublishedAt = null,

                RetryCount = 0
            };

            _db.OutboxMessages.Add(outboxMessage);

            await _db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Transaction {TransactionNumber} created successfully",
                trx.TransactionNumber);

            return trx;
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }
    }
}