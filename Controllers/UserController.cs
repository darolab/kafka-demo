using KafkaDemo.DTO;
using KafkaDemo.Repository;
using KafkaDemo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KafkaDemo.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[ServiceFilter(typeof(ControllerAspect))]
[Route("api/[controller]")]
public sealed class UserController(
    AuthenticationRepository authenticationRepository,
    AuthenticationService authenticationService,
    UserRepository userRepository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest("Page must be positive and pageSize must be between 1 and 100.");

        return Ok(await authenticationRepository.GetUsersAsync(
            page,
            pageSize,
            cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetUser(int id, CancellationToken cancellationToken)
    {
        var user = await authenticationRepository.GetUserAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(
        CreateLegacyUserRequestDTO request,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await authenticationRepository.CreateLegacyUserAsync(
                request,
                DateTime.UtcNow,
                cancellationToken);
            return StatusCode(StatusCodes.Status201Created, user);
        }
        catch (AuthenticationConflictException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "An account with that email already exists."
            });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateUser(
        int id,
        UpdateUserProfileRequestDTO request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authenticationRepository.UpdateUserProfileAsync(
                id,
                request,
                cancellationToken));
        }
        catch (AccountNotFoundException)
        {
            return NotFound();
        }
        catch (AuthenticationConflictException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "An account with that email already exists."
            });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteUser(int id, CancellationToken cancellationToken)
    {
        try
        {
            await authenticationRepository.DeleteUserAsync(id, cancellationToken);
            return NoContent();
        }
        catch (AccountNotFoundException)
        {
            return NotFound();
        }
        catch (AccountStateConflictException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The last enabled administrator cannot be removed."
            });
        }
    }

    [HttpGet("/{id:int}")]
    public async Task<IActionResult> GetUserByProcedure(
        int id,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetUserByIdAsync(id);
        if (user is null)
            return NotFound();

        return Ok(new UserResponseDTO(
            user.Id,
            user.Username ?? string.Empty,
            user.Email,
            user.Name,
            user.Age,
            user.CreatedAt,
            []));
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("The q query parameter is required.");

        return Ok(await authenticationRepository.SearchUsersAsync(q, cancellationToken));
    }

    [HttpPost("{id:int}/roles")]
    public async Task<IActionResult> AssignRole(
        int id,
        AssignRoleRequestDTO request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authenticationRepository.AssignRoleAsync(
                id,
                request.Role,
                cancellationToken));
        }
        catch (AccountNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException)
        {
            return BadRequest("The requested role is not supported.");
        }
    }

    [HttpDelete("{id:int}/roles/{role}")]
    public async Task<IActionResult> RemoveRole(
        int id,
        string role,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authenticationRepository.RemoveRoleAsync(id, role, cancellationToken));
        }
        catch (AccountNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException)
        {
            return BadRequest("The requested role is not supported.");
        }
        catch (AccountStateConflictException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The role change would violate an account or administrator requirement."
            });
        }
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> SetStatus(
        int id,
        AccountStatusRequestDTO request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authenticationRepository.SetAccountStatusAsync(
                id,
                request.Enabled,
                cancellationToken));
        }
        catch (AccountNotFoundException)
        {
            return NotFound();
        }
        catch (AccountStateConflictException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The requested status would violate an account or administrator requirement."
            });
        }
    }

    [HttpPost("{id:int}/credentials")]
    public async Task<IActionResult> ProvisionCredentials(
        int id,
        ProvisionCredentialsRequestDTO request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authenticationService.ProvisionLegacyAccountAsync(
                id,
                request,
                cancellationToken));
        }
        catch (AccountNotFoundException)
        {
            return NotFound();
        }
        catch (AuthenticationConflictException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The username or email is already in use."
            });
        }
        catch (AccountStateConflictException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The account is not eligible for credential provisioning."
            });
        }
    }
}
