using KafkaDemo.Data;
using KafkaDemo.DTO;
using KafkaDemo.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KafkaDemo.Repository;

public sealed class AuthenticationRepository(AppDbContext dbContext)
{
    public async Task<UserResponseDTO> CompleteBootstrapAsync(
        User user,
        DateTime completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        var claimed = await dbContext.AuthenticationBootstrapStates
            .Where(state => state.Id == 1 && state.CompletedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    state => state.CompletedAtUtc,
                    completedAtUtc),
                cancellationToken);
        if (claimed != 1)
            throw new AuthenticationBootstrapConflictException();

        var adminRole = await dbContext.Roles
            .SingleAsync(role => role.Name == "Admin", cancellationToken);
        user.UserRoles.Add(new UserRole { User = user, Role = adminRole });
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToSafeResponse(user);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw new AuthenticationConflictException();
        }
    }

    public Task<bool> UserExistsAsync(
        string normalizedUsername,
        string normalizedEmail,
        CancellationToken cancellationToken = default) =>
        dbContext.Users.AnyAsync(
            user => user.NormalizedUsername == normalizedUsername ||
                    user.NormalizedEmail == normalizedEmail,
            cancellationToken);

    public async Task CreateRegisteredUserAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        var userRole = await dbContext.Roles
            .SingleAsync(role => role.Name == "User", cancellationToken);
        user.UserRoles.Add(new UserRole { User = user, Role = userRole });
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            throw new AuthenticationConflictException();
        }
    }

    public Task<User?> FindByLoginAsync(
        string normalizedLogin,
        CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(
                user => user.NormalizedUsername == normalizedLogin ||
                        user.NormalizedEmail == normalizedLogin,
                cancellationToken);

    public Task<AuthenticatedAccount?> FindEnabledAccountAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.IsEnabled)
            .Select(user => new AuthenticatedAccount(
                user.Id,
                user.Username ?? user.Name,
                user.UserRoles.Select(userRole => userRole.Role.Name).ToArray()))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedUserResponseDTO> GetUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsNoTracking().OrderBy(user => user.Id);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new UserResponseDTO(
                user.Id,
                user.Username ?? string.Empty,
                user.Email,
                user.Name,
                user.Age,
                user.CreatedAt,
                user.UserRoles.Select(link => link.Role.Name).ToArray()))
            .ToListAsync(cancellationToken);

        return new PagedUserResponseDTO(items, page, pageSize, totalCount);
    }

    public async Task<UserResponseDTO?> GetUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(account => account.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleOrDefaultAsync(account => account.Id == userId, cancellationToken);
        return user is null ? null : ToSafeResponse(user);
    }

    public async Task<IReadOnlyList<UserResponseDTO>> SearchUsersAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var pattern = $"%{query.Trim()}%";
        var users = await dbContext.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .Where(user => EF.Functions.Like(user.Name, pattern) ||
                           EF.Functions.Like(user.Email, pattern) ||
                           EF.Functions.Like(user.Username ?? string.Empty, pattern))
            .OrderBy(user => user.Id)
            .ToListAsync(cancellationToken);
        return users.Select(ToSafeResponse).ToArray();
    }

    public async Task<UserResponseDTO> CreateLegacyUserAsync(
        CreateLegacyUserRequestDTO request,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim();
        var user = new User
        {
            Name = request.FullName.Trim(),
            Email = email,
            NormalizedEmail = Normalize(email),
            Age = request.Age,
            CreatedAt = createdAtUtc,
            IsEnabled = false
        };
        dbContext.Users.Add(user);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            throw new AuthenticationConflictException();
        }

        return ToSafeResponse(user);
    }

    public async Task<UserResponseDTO> UpdateUserProfileAsync(
        int userId,
        UpdateUserProfileRequestDTO request,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .Include(account => account.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleOrDefaultAsync(account => account.Id == userId, cancellationToken)
            ?? throw new AccountNotFoundException();

        var email = request.Email.Trim();
        user.Name = request.FullName.Trim();
        user.Email = email;
        user.NormalizedEmail = Normalize(email);
        user.Age = request.Age;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            throw new AuthenticationConflictException();
        }

        return ToSafeResponse(user);
    }

    public async Task DeleteUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var user = await dbContext.Users
            .Include(account => account.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleOrDefaultAsync(account => account.Id == userId, cancellationToken)
            ?? throw new AccountNotFoundException();

        if (user.IsEnabled && user.UserRoles.Any(link => link.Role.Name == "Admin") &&
            await CountOtherEnabledAdminsAsync(userId, cancellationToken) == 0)
        {
            throw new AccountStateConflictException();
        }

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<UserResponseDTO> AssignRoleAsync(
        int userId,
        string roleName,
        CancellationToken cancellationToken = default) =>
        UpdateRolesAsync(userId, roleName, add: true, cancellationToken);

    public Task<UserResponseDTO> RemoveRoleAsync(
        int userId,
        string roleName,
        CancellationToken cancellationToken = default) =>
        UpdateRolesAsync(userId, roleName, add: false, cancellationToken);

    private async Task<UserResponseDTO> UpdateRolesAsync(
        int userId,
        string roleName,
        bool add,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var user = await LoadUserForUpdateAsync(userId, cancellationToken);
        var role = await dbContext.Roles
            .SingleOrDefaultAsync(item => item.Name == roleName, cancellationToken);
        if (role is null)
            throw new ArgumentException("Unsupported role.", nameof(roleName));

        var link = user.UserRoles.SingleOrDefault(item => item.RoleId == role.Id);
        if (add && link is null)
        {
            user.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id, Role = role });
        }
        else if (!add && link is not null)
        {
            if (user.IsEnabled && user.UserRoles.Count == 1)
                throw new AccountStateConflictException();

            if (user.IsEnabled && role.Name == "Admin" &&
                await CountOtherEnabledAdminsAsync(userId, cancellationToken) == 0)
            {
                throw new AccountStateConflictException();
            }

            dbContext.UserRoles.Remove(link);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSafeResponse(user);
    }

    public async Task<UserResponseDTO> SetAccountStatusAsync(
        int userId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var user = await LoadUserForUpdateAsync(userId, cancellationToken);
        if (enabled && user.UserRoles.Count == 0)
            throw new AccountStateConflictException();

        if (!enabled && user.IsEnabled &&
            user.UserRoles.Any(link => link.Role.Name == "Admin") &&
            await CountOtherEnabledAdminsAsync(userId, cancellationToken) == 0)
        {
            throw new AccountStateConflictException();
        }

        user.IsEnabled = enabled;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSafeResponse(user);
    }

    public async Task<UserResponseDTO> ProvisionLegacyAccountAsync(
        int userId,
        string username,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var user = await LoadUserForUpdateAsync(userId, cancellationToken);
        if (user.IsEnabled || user.Username is not null || user.PasswordHash is not null)
            throw new AccountStateConflictException();

        user.Username = username.Trim();
        user.NormalizedUsername = Normalize(username);
        user.PasswordHash = passwordHash;
        user.IsEnabled = true;
        var userRole = await dbContext.Roles
            .SingleAsync(role => role.Name == "User", cancellationToken);
        user.UserRoles.Add(new UserRole { UserId = userId, RoleId = userRole.Id, Role = userRole });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw new AuthenticationConflictException();
        }

        return ToSafeResponse(user);
    }

    private async Task<User> LoadUserForUpdateAsync(
        int userId,
        CancellationToken cancellationToken) =>
        await dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken)
        ?? throw new AccountNotFoundException();

    private Task<int> CountOtherEnabledAdminsAsync(
        int excludedUserId,
        CancellationToken cancellationToken) =>
        dbContext.Users.CountAsync(
            user => user.Id != excludedUserId &&
                    user.IsEnabled &&
                    user.UserRoles.Any(link => link.Role.Name == "Admin"),
            cancellationToken);

    public async Task<bool> RecordFailedLoginAsync(
        int userId,
        DateTime nowUtc,
        int maxFailedAttempts,
        TimeSpan lockoutDuration,
        CancellationToken cancellationToken = default)
    {
        var lockoutUntil = nowUtc.Add(lockoutDuration);

        var affectedRows = await dbContext.Users
            .Where(user =>
                user.Id == userId &&
                user.IsEnabled &&
                user.PasswordHash != null &&
                (user.LockoutUntilUtc == null || user.LockoutUntilUtc <= nowUtc))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        user => user.FailedLoginCount,
                        user => user.LockoutUntilUtc != null &&
                                user.LockoutUntilUtc <= nowUtc
                            ? 1
                            : user.FailedLoginCount + 1)
                    .SetProperty(
                        user => user.LockoutUntilUtc,
                        user => user.LockoutUntilUtc != null &&
                                user.LockoutUntilUtc <= nowUtc
                            ? (maxFailedAttempts <= 1 ? lockoutUntil : null)
                            : user.FailedLoginCount + 1 >= maxFailedAttempts
                                ? lockoutUntil
                                : null),
                cancellationToken);

        return affectedRows == 1;
    }

    public async Task<bool> CompleteSuccessfulLoginAsync(
        User user,
        string? updatedPasswordHash,
        CancellationToken cancellationToken = default)
    {
        var affectedRows = await dbContext.Users
            .Where(current =>
                current.Id == user.Id &&
                current.IsEnabled &&
                current.PasswordHash == user.PasswordHash)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        current => current.PasswordHash,
                        updatedPasswordHash ?? user.PasswordHash!)
                    .SetProperty(current => current.FailedLoginCount, 0)
                    .SetProperty(current => current.LockoutUntilUtc, (DateTime?)null),
                cancellationToken);

        return affectedRows == 1;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        var cause = exception.GetBaseException();
        return cause switch
        {
            SqlException sqlException => sqlException.Number is 2601 or 2627,
            _ when cause.GetType().Name == "SqliteException" =>
                cause.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal),
            _ => false
        };
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static UserResponseDTO ToSafeResponse(User user) =>
        new(
            user.Id,
            user.Username ?? string.Empty,
            user.Email,
            user.Name,
            user.Age,
            user.CreatedAt,
            user.UserRoles.Select(link => link.Role.Name).ToArray());
}

public sealed record AuthenticatedAccount(int Id, string Name, string[] Roles);
public sealed record PagedUserResponseDTO(
    IReadOnlyList<UserResponseDTO> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed class AuthenticationConflictException : Exception;
public sealed class AuthenticationBootstrapConflictException : Exception;
public sealed class AccountNotFoundException : Exception;
public sealed class AccountStateConflictException : Exception;
