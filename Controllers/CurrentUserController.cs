using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using KafkaDemo.DTO;
using KafkaDemo.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KafkaDemo.Controllers;

[ApiController]
[Authorize]
[Route("api/user")]
public sealed class CurrentUserController(
    AuthenticationRepository authenticationRepository) : ControllerBase
{
    /// <summary>Returns the authenticated user's profile and assigned roles.</summary>
    [HttpGet("getUserDetail")]
    [ProducesResponseType(typeof(UserResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponseDTO>> GetUserDetail(
        CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst("userId")?.Value;

        if (!int.TryParse(
                userIdValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var userId) ||
            userId <= 0)
        {
            return Unauthorized();
        }

        var user = await authenticationRepository.GetUserAsync(
            userId,
            cancellationToken);

        return user is null ? NotFound() : Ok(user);
    }
}
