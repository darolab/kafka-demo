using System.Net;
using System.Net.Http.Headers;
using KafkaDemo.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KafkaDemo.Data;
using Xunit;

namespace KafkaDemo.Tests.Services;

public sealed class CurrentUserAuthorizationTests
{
    [Fact]
    public async Task PersistedRolesOverrideTokenRolesAndChangesApplyOnNextRequest()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        await factory.SeedUserAsync(42, "casey", ["User"]);
        using var client = factory.CreateClient();

        var tokenClaimsAdmin = factory.CreateToken(userId: 42, roles: ["Admin"]);
        var tokenClaimsUser = factory.CreateToken(userId: 42, roles: ["User"]);

        var forgedAdmin = await GetUsersAsync(client, tokenClaimsAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, forgedAdmin.StatusCode);

        await factory.SetUserRolesAsync(42, "Admin");
        var persistedAdmin = await GetUsersAsync(client, tokenClaimsUser);
        Assert.Equal(HttpStatusCode.OK, persistedAdmin.StatusCode);
        var usersJson = await persistedAdmin.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordhash", usersJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("normalizedemail", usersJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failedlogincount", usersJson, StringComparison.OrdinalIgnoreCase);

        await factory.SetUserRolesAsync(42, "User");
        var removedAdmin = await GetUsersAsync(client, tokenClaimsAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, removedAdmin.StatusCode);
    }

    [Fact]
    public async Task DisabledOrDeletedAccountIsRejectedEvenWhenTokenIsValid()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        await factory.SeedUserAsync(42, "casey", ["Admin"]);
        using var client = factory.CreateClient();
        var token = factory.CreateToken(userId: 42, roles: ["Admin"]);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(account => account.Id == 42);
            user.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetUsersAsync(client, token)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(account => account.Id == 42);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetUsersAsync(client, token)).StatusCode);
    }

    private static async Task<HttpResponseMessage> GetUsersAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
