using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using KafkaDemo.DTO;
using KafkaDemo.Models;
using KafkaDemo.Repository;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace KafkaDemo.Services;

public sealed class AuthenticationService
{
    private readonly AuthenticationRepository _repository;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly AuthenticationOptions _authenticationOptions;
    private readonly JwtOptions _jwtOptions;
    private readonly TimeProvider _timeProvider;
    private readonly string _dummyPasswordHash;

    public AuthenticationService(
        AuthenticationRepository repository,
        IPasswordHasher<User> passwordHasher,
        IOptions<AuthenticationOptions> authenticationOptions,
        IOptions<JwtOptions> jwtOptions,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _authenticationOptions = authenticationOptions.Value;
        _jwtOptions = jwtOptions.Value;
        _timeProvider = timeProvider;
        _dummyPasswordHash = passwordHasher.HashPassword(
            new User(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public async Task<UserResponseDTO> RegisterAsync(
        RegistrationRequestDTO request,
        CancellationToken cancellationToken = default)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim();
        var normalizedUsername = Normalize(username);
        var normalizedEmail = Normalize(email);

        if (await _repository.UserExistsAsync(
                normalizedUsername,
                normalizedEmail,
                cancellationToken))
        {
            throw new AuthenticationConflictException();
        }

        var user = new User
        {
            Name = request.FullName.Trim(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            Username = username,
            NormalizedUsername = normalizedUsername,
            IsEnabled = true,
            FailedLoginCount = 0,
            Age = request.Age,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        await _repository.CreateRegisteredUserAsync(user, cancellationToken);

        return ToSafeResponse(user);
    }

    public async Task<UserResponseDTO> BootstrapAdminAsync(
        BootstrapRequestDTO request,
        string? suppliedSecret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_authenticationOptions.BootstrapSecret))
            throw new BootstrapNotConfiguredException();

        var configuredDigest = SHA256.HashData(
            Encoding.UTF8.GetBytes(_authenticationOptions.BootstrapSecret));
        var suppliedDigest = SHA256.HashData(
            Encoding.UTF8.GetBytes(suppliedSecret ?? string.Empty));
        if (!CryptographicOperations.FixedTimeEquals(configuredDigest, suppliedDigest))
            throw new BootstrapSecretException();

        var username = request.Username.Trim();
        var email = request.Email.Trim();
        var user = new User
        {
            Name = request.FullName.Trim(),
            Email = email,
            NormalizedEmail = Normalize(email),
            Username = username,
            NormalizedUsername = Normalize(username),
            IsEnabled = true,
            FailedLoginCount = 0,
            Age = request.Age,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        return await _repository.CompleteBootstrapAsync(
            user,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }

    public Task<UserResponseDTO> ProvisionLegacyAccountAsync(
        int userId,
        ProvisionCredentialsRequestDTO request,
        CancellationToken cancellationToken = default)
    {
        var username = request.Username.Trim();
        var user = new User { Id = userId, Username = username };
        var passwordHash = _passwordHasher.HashPassword(user, request.Password);
        return _repository.ProvisionLegacyAccountAsync(
            userId,
            username,
            passwordHash,
            cancellationToken);
    }

    public Task<PagedUserResponseDTO> GetUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        return _repository.GetUsersAsync(page, pageSize, cancellationToken);
    }

    public async Task<LoginResponseDTO?> LoginAsync(
        LoginRequestDTO request,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var normalizedLogin = Normalize(request.Username);
        var user = await _repository.FindByLoginAsync(normalizedLogin, cancellationToken);

        if (user?.PasswordHash is null)
        {
            _passwordHasher.VerifyHashedPassword(
                new User(),
                _dummyPasswordHash,
                request.Password);
            return null;
        }

        var verification = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);
        if (!user.IsEnabled || user.LockoutUntilUtc > now ||
            verification == PasswordVerificationResult.Failed)
        {
            if (user.IsEnabled &&
                (user.LockoutUntilUtc is null || user.LockoutUntilUtc <= now))
            {
                await _repository.RecordFailedLoginAsync(
                    user.Id,
                    now,
                    _authenticationOptions.MaxFailedLoginAttempts,
                    TimeSpan.FromMinutes(_authenticationOptions.LockoutMinutes),
                    cancellationToken);
            }

            return null;
        }

        var updatedHash = verification == PasswordVerificationResult.SuccessRehashNeeded
            ? _passwordHasher.HashPassword(user, request.Password)
            : null;

        if (!await _repository.CompleteSuccessfulLoginAsync(
                user,
                updatedHash,
                cancellationToken))
        {
            return null;
        }

        return CreateToken(user, now);
    }

    private LoginResponseDTO CreateToken(User user, DateTime nowUtc)
    {
        var expiresAtUtc = nowUtc.AddMinutes(_jwtOptions.TokenLifetimeMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("userId", user.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, user.Username ?? user.Name)
        };

        claims.AddRange(user.UserRoles.Select(userRole =>
            new Claim("role", userRole.Role.Name)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: nowUtc,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new LoginResponseDTO(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAtUtc);
    }

    private static UserResponseDTO ToSafeResponse(User user) =>
        new(
            user.Id,
            user.Username!,
            user.Email,
            user.Name,
            user.Age,
            user.CreatedAt,
            user.UserRoles.Select(userRole => userRole.Role.Name).ToArray());

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

public sealed class BootstrapNotConfiguredException : Exception;
public sealed class BootstrapSecretException : Exception;
