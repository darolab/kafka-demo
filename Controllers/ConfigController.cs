using Microsoft.AspNetCore.Mvc;
using KafkaDemo.Services;

namespace VaultDemo.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly VaultService _vaultService;

    public ConfigController(VaultService vaultService)
    {
        _vaultService = vaultService;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var secret = await _vaultService.GetSecretAsync();

        return Ok(secret);
    }
}