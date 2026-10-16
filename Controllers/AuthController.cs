using KafkaDemo.DTO;
using KafkaDemo.Repository;
using KafkaDemo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AuthenticationService authenticationService,
    ILogger<AuthController> logger) : ControllerBase
{

    
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegistrationRequestDTO request,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await authenticationService.RegisterAsync(request, cancellationToken);
            logger.LogInformation("User registration succeeded.");
            return StatusCode(StatusCodes.Status201Created, user);
        }
        catch (AuthenticationConflictException)
        {
            logger.LogWarning("User registration failed because the account conflicts with existing account data.");
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "An account with that username or email already exists."
            });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequestDTO request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.LoginAsync(request, cancellationToken);
        if (result is null)
        {
            logger.LogWarning("User login failed because the supplied credentials were invalid.");
            return Unauthorized("Invalid username or password");
        }

        logger.LogInformation("User login succeeded.");
        return Ok(result);
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap(
        BootstrapRequestDTO request,
        [FromHeader(Name = "X-Authentication-Bootstrap")] string? bootstrapSecret,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await authenticationService.BootstrapAdminAsync(
                request,
                bootstrapSecret,
                cancellationToken);
            logger.LogInformation("Administrator bootstrap succeeded.");
            return StatusCode(StatusCodes.Status201Created, user);
        }
        catch (BootstrapNotConfiguredException)
        {
            logger.LogWarning("Administrator bootstrap failed because bootstrap is not configured.");
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Administrator bootstrap is not configured.");
        }
        catch (BootstrapSecretException)
        {
            logger.LogWarning("Administrator bootstrap failed because the supplied bootstrap credentials were invalid.");
            return Unauthorized("Invalid bootstrap credentials.");
        }
        catch (AuthenticationBootstrapConflictException)
        {
            logger.LogWarning("Administrator bootstrap failed because bootstrap has already been completed.");
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Administrator bootstrap is already complete."
            });
        }
        catch (AuthenticationConflictException)
        {
            logger.LogWarning("Administrator bootstrap failed because the account conflicts with existing account data.");
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The account conflicts with existing account data."
            });
        }
    }
}
