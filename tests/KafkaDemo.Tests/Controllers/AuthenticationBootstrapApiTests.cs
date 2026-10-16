using System.Net;
using System.Net.Http.Json;
using KafkaDemo.Data;
using KafkaDemo.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KafkaDemo.Tests.Controllers;

public sealed class AuthenticationBootstrapApiTests
{
    [Fact]
    public async Task BootstrapRequiresCorrectSecretAndCreatesOnlyOneAdmin()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var invalidSecret = await SendBootstrapAsync(client, "wrong-secret", "admin-one");
        Assert.Equal(HttpStatusCode.Unauthorized, invalidSecret.StatusCode);

        var validSecret = await SendBootstrapAsync(
            client,
            AuthenticationTestFactory.BootstrapSecret,
            "admin-one");
        Assert.Equal(HttpStatusCode.Created, validSecret.StatusCode);
        var responseJson = await validSecret.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PasswordHash", responseJson, StringComparison.OrdinalIgnoreCase);

        var replay = await SendBootstrapAsync(
            client,
            AuthenticationTestFactory.BootstrapSecret,
            "admin-two");
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(
            1,
            await db.UserRoles.CountAsync(link => link.Role.Name == "Admin"));
        Assert.NotNull((await db.AuthenticationBootstrapStates.SingleAsync()).CompletedAtUtc);
    }

    [Fact]
    public async Task ConcurrentBootstrapRequestsCreateExactlyOneAdmin()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var requests = new[]
        {
            SendBootstrapAsync(firstClient, AuthenticationTestFactory.BootstrapSecret, "racer-one"),
            SendBootstrapAsync(secondClient, AuthenticationTestFactory.BootstrapSecret, "racer-two")
        };
        var responses = await Task.WhenAll(requests);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.UserRoles.CountAsync(link => link.Role.Name == "Admin"));
    }

    [Fact]
    public async Task BootstrapFailureRollsBackCompletionMarkerForSafeRetry()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username = "taken",
                email = "taken@example.test",
                fullName = "Existing User",
                password = "existing-password-long"
            });

        var duplicate = await SendBootstrapAsync(
            client,
            AuthenticationTestFactory.BootstrapSecret,
            "taken");
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var retry = await SendBootstrapAsync(
            client,
            AuthenticationTestFactory.BootstrapSecret,
            "first-admin");
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendBootstrapAsync(
        HttpClient client,
        string secret,
        string username)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/bootstrap")
        {
            Content = JsonContent.Create(new
            {
                username,
                email = $"{username}@example.test",
                fullName = $"Test {username}",
                password = "bootstrap-password-long"
            })
        };
        request.Headers.Add("X-Authentication-Bootstrap", secret);
        return await client.SendAsync(request);
    }
}
