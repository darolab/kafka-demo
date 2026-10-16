using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KafkaDemo.Data;
using KafkaDemo.DTO;
using KafkaDemo.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KafkaDemo.Tests.Controllers;

public sealed class UserManagementApiTests
{
    [Fact]
    public async Task AnonymousAndRegularUsersCannotManageUsers()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/user")).StatusCode);

        await RegisterAndLoginAsync(client, "ordinary", "ordinary@example.test");
        var userToken = await LoginAsync(client, "ordinary");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", userToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/user")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/sql-users")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/user/1/roles", new { role = "Admin" })).StatusCode);
    }

    [Fact]
    public async Task AdminCanListReadAssignRemoveAndChangeUserStatusSafely()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await BootstrapAdminAsync(client, "admin", "admin@example.test");
        var adminToken = await LoginAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);
        var target = await RegisterAndLoginAsync(client, "member", "member@example.test");

        var list = await client.GetAsync("/api/user?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(1, listJson.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(5, listJson.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, listJson.RootElement.GetProperty("totalCount").GetInt32());
        AssertSafeUserJson(listJson.RootElement.GetProperty("items").ToString());

        var details = await client.GetAsync($"/api/user/{target.Id}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        AssertSafeUserJson(await details.Content.ReadAsStringAsync());

        var assigned = await client.PostAsJsonAsync(
            $"/api/user/{target.Id}/roles",
            new { role = "Admin" });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Contains("Admin", await assigned.Content.ReadAsStringAsync());

        var removed = await client.DeleteAsync($"/api/user/{target.Id}/roles/Admin");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        var disabled = await client.PatchAsJsonAsync(
            $"/api/user/{target.Id}/status",
            new { enabled = false });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        var enabled = await client.PatchAsJsonAsync(
            $"/api/user/{target.Id}/status",
            new { enabled = true });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var targetUser = await db.Users
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleAsync(user => user.Id == target.Id);
        Assert.True(targetUser.IsEnabled);
        Assert.Equal(["User"], targetUser.UserRoles.Select(link => link.Role.Name));
    }

    [Fact]
    public async Task AdminCannotRemoveLastRoleOrDisableLastEnabledAdmin()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var adminId = await BootstrapAdminAsync(client, "solo-admin", "solo-admin@example.test");
        var token = await LoginAsync(client, "solo-admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var removeLastRole = await client.DeleteAsync($"/api/user/{adminId}/roles/Admin");
        Assert.Equal(HttpStatusCode.Conflict, removeLastRole.StatusCode);

        var disableLastAdmin = await client.PatchAsJsonAsync(
            $"/api/user/{adminId}/status",
            new { enabled = false });
        Assert.Equal(HttpStatusCode.Conflict, disableLastAdmin.StatusCode);
    }

    [Fact]
    public async Task AdminCanProvisionLegacyAccountAndCannotProvisionItTwice()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await BootstrapAdminAsync(client, "admin", "admin@example.test");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginAsync(client, "admin"));

        var createLegacy = await client.PostAsJsonAsync(
            "/api/user",
            new { fullName = "Legacy User", email = "legacy@example.test", age = 40 });
        Assert.Equal(HttpStatusCode.Created, createLegacy.StatusCode);
        using var legacyJson = JsonDocument.Parse(await createLegacy.Content.ReadAsStringAsync());
        var legacyId = legacyJson.RootElement.GetProperty("id").GetInt32();
        AssertSafeUserJson(legacyJson.RootElement.ToString());

        var provision = await client.PostAsJsonAsync(
            $"/api/user/{legacyId}/credentials",
            new { username = "legacy-login", password = "legacy-password-very-long" });
        Assert.Equal(HttpStatusCode.OK, provision.StatusCode);
        AssertSafeUserJson(await provision.Content.ReadAsStringAsync());

        var duplicateProvision = await client.PostAsJsonAsync(
            $"/api/user/{legacyId}/credentials",
            new { username = "legacy-login", password = "legacy-password-very-long" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateProvision.StatusCode);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "legacy-login", password = "legacy-password-very-long" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    private static async Task<UserResponseDTO> RegisterAndLoginAsync(
        HttpClient client,
        string username,
        string email)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username,
                email,
                fullName = $"Test {username}",
                password = "test-password-at-least-12"
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserResponseDTO>())!;
    }

    private static async Task<int> BootstrapAdminAsync(
        HttpClient client,
        string username,
        string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/bootstrap")
        {
            Content = JsonContent.Create(new
            {
                username,
                email,
                fullName = $"Test {username}",
                password = "admin-password-at-least-12"
            })
        };
        request.Headers.Add(
            "X-Authentication-Bootstrap",
            AuthenticationTestFactory.BootstrapSecret);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserResponseDTO>())!.Id;
    }

    private static async Task<string> LoginAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username,
                password = username == "legacy-login"
                    ? "legacy-password-very-long"
                    : username.Contains("admin", StringComparison.Ordinal)
                        ? "admin-password-at-least-12"
                        : "test-password-at-least-12"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private static void AssertSafeUserJson(string json)
    {
        Assert.DoesNotContain("passwordhash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failedlogincount", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lockoutuntilutc", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("normalizedemail", json, StringComparison.OrdinalIgnoreCase);
    }
}
