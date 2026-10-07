using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KafkaDemo;
using KafkaDemo.DTO;
using KafkaDemo.Models;
using KafkaDemo.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace KafkaDemo.Tests.Controllers;

public sealed class TransactionsControllerTests
{
    [Fact]
    public async Task CreateKeepsExistingSuccessResponseShape()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:SigningKey"] = "test-only-notification-signing-key-32-bytes"
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.RemoveAll<ITransactionService>();
                    services.AddSingleton<ITransactionService, SuccessfulTransactionService>();
                });
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/Transactions",
            new CreateTransactionRequest
            {
                FromAccount = "1001",
                ToAccount = "1002",
                Amount = 12.50m,
                Currency = "USD"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Transaction created successfully",
            body.RootElement.GetProperty("message").GetString());
        Assert.Equal(
            "TXN-TEST",
            body.RootElement.GetProperty("data").GetProperty("transactionNumber").GetString());
    }

    private sealed class SuccessfulTransactionService : ITransactionService
    {
        public Task<Transaction> CreateAsync(
            CreateTransactionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Transaction
            {
                Id = 1,
                TransactionNumber = "TXN-TEST",
                FromAccount = request.FromAccount,
                ToAccount = request.ToAccount,
                Amount = request.Amount,
                Currency = request.Currency,
                Status = "SUCCESS",
                CreatedAt = DateTime.UtcNow
            });
    }
}