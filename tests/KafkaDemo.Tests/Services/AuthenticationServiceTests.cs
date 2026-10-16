using KafkaDemo.Data;
using KafkaDemo.DTO;
using KafkaDemo.Models;
using KafkaDemo.Services;
using KafkaDemo.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KafkaDemo.Tests.Services;

public sealed class AuthenticationServiceTests
{
    [Fact]
    public async Task RegistrationHashesPasswordAndNormalizesIdentifiers()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AuthenticationService>();

        var result = await service.RegisterAsync(new RegistrationRequestDTO
        {
            Username = "  casey  ",
            Email = " Casey@Example.Test ",
            FullName = " Casey Example ",
            Password = "correct-horse-battery-staple"
        });

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync();
        Assert.Equal("CASEY", user.NormalizedUsername);
        Assert.Equal("CASEY@EXAMPLE.TEST", user.NormalizedEmail);
        Assert.Equal("casey", result.Username);
        Assert.DoesNotContain("correct-horse-battery-staple", user.PasswordHash);
        Assert.NotNull(user.PasswordHash);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                "correct-horse-battery-staple"));
    }

    [Fact]
    public async Task FailedLoginIsGenericAndIncrementsPersistentFailureCount()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AuthenticationService>();

        await service.RegisterAsync(new RegistrationRequestDTO
        {
            Username = "casey",
            Email = "casey@example.test",
            FullName = "Casey Example",
            Password = "correct-horse-battery-staple"
        });

        var unknownUser = await service.LoginAsync(new LoginRequestDTO
        {
            Username = "missing",
            Password = "incorrect-password"
        });
        var incorrectPassword = await service.LoginAsync(new LoginRequestDTO
        {
            Username = "casey",
            Password = "incorrect-password"
        });

        Assert.Null(unknownUser);
        Assert.Null(incorrectPassword);

        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync();
        Assert.Equal(1, user.FailedLoginCount);
    }

    [Fact]
    public async Task LoginRehashesCredentialWhenHasherRequestsIt()
    {
        var passwordHasher = new RehashingPasswordHasher();
        await using var factory = new AuthenticationTestFactory(passwordHasher);
        await factory.InitializeDatabaseAsync();

        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = await db.Roles.SingleAsync(item => item.Name == "User");
            db.Users.Add(new User
            {
                Name = "Casey Example",
                Email = "casey@example.test",
                NormalizedEmail = "CASEY@EXAMPLE.TEST",
                Username = "casey",
                NormalizedUsername = "CASEY",
                PasswordHash = "old-password-hash",
                IsEnabled = true,
                CreatedAt = factory.Clock.GetUtcNow().UtcDateTime,
                UserRoles = [new UserRole { Role = role }]
            });
            await db.SaveChangesAsync();
        }

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AuthenticationService>();
        var result = await service.LoginAsync(new LoginRequestDTO
        {
            Username = "casey",
            Password = "correct-password"
        });

        Assert.NotNull(result);

        using var verifyScope = factory.Services.CreateScope();
        var dbAfterLogin = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userAfterLogin = await dbAfterLogin.Users.SingleAsync();
        Assert.Equal("rehash-password-hash", userAfterLogin.PasswordHash);
    }

    private sealed class RehashingPasswordHasher : IPasswordHasher<User>
    {
        public string HashPassword(User user, string password) =>
            password == "correct-password"
                ? "rehash-password-hash"
                : "dummy-password-hash";

        public PasswordVerificationResult VerifyHashedPassword(
            User user,
            string hashedPassword,
            string providedPassword) =>
            hashedPassword == "old-password-hash"
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
    }
}
