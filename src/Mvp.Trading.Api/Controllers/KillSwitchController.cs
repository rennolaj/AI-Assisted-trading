using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Mvp.Trading.Api.Models;
using Mvp.Trading.Execution;

namespace Mvp.Trading.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class KillSwitchController : ControllerBase
{
    private readonly IKillSwitchService _killSwitchService;
    private readonly KillSwitchApiOptions _options;
    private readonly ILogger<KillSwitchController> _logger;

    public KillSwitchController(
        IKillSwitchService killSwitchService,
        IOptions<KillSwitchApiOptions> options,
        ILogger<KillSwitchController> logger)
    {
        _killSwitchService = killSwitchService;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var status = await _killSwitchService.GetStatusAsync(ct);
        return Ok(status);
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] KillSwitchActivationRequest request, CancellationToken ct)
    {
        // Validate secret
        if (string.IsNullOrEmpty(request.Secret) || request.Secret != _options.Secret)
        {
            _logger.LogWarning("Invalid kill switch secret provided from {IP}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid secret" });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "Reason is required" });
        }

        await _killSwitchService.ActivateAsync(request.Level, request.Reason, request.ActivatedBy ?? "API", ct);

        return Ok(new { message = "Kill switch activated", level = request.Level, reason = request.Reason });
    }

    [HttpPost("deactivate")]
    public async Task<IActionResult> Deactivate([FromBody] KillSwitchDeactivationRequest request, CancellationToken ct)
    {
        // Validate secret
        if (string.IsNullOrEmpty(request.Secret) || request.Secret != _options.Secret)
        {
            _logger.LogWarning("Invalid kill switch secret provided from {IP}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid secret" });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "Reason is required" });
        }

        await _killSwitchService.DeactivateAsync(request.DeactivatedBy ?? "API", request.Reason, ct);

        return Ok(new { message = "Kill switch deactivated", reason = request.Reason });
    }
}
