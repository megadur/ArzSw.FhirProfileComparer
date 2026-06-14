using ArzSw.FhirProfileComparer.Api.Models;
using ArzSw.FhirProfileComparer.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace ArzSw.FhirProfileComparer.Api.Controllers;

[ApiController]
[Route("api/fhir/[controller]")]
public class CompareController : ControllerBase
{
    private readonly ILogger<CompareController> _logger;
    private readonly ProfileComparerService _comparerService;

    public CompareController(ILogger<CompareController> logger, ProfileComparerService comparerService)
    {
        _logger = logger;
        _comparerService = comparerService;
    }

    [HttpGet]
    public async Task<ActionResult<CompareResult>> GetCompareResult([FromQuery] string packageId, [FromQuery] string sourceVersion, [FromQuery] string targetVersion)
    {
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(sourceVersion) || string.IsNullOrWhiteSpace(targetVersion))
        {
            return BadRequest("packageId, sourceVersion, and targetVersion are required.");
        }

        try
        {
            var result = await _comparerService.CompareAsync(packageId, sourceVersion, targetVersion);
            return Ok(result);
        }
        catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Package version not found: {PackageId} {SourceVersion} {TargetVersion}", packageId, sourceVersion, targetVersion);
            return BadRequest(new { Error = $"Das Paket '{packageId}' konnte in der Version '{sourceVersion}' oder '{targetVersion}' auf Simplifier.net nicht gefunden werden." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare profiles.");
            return StatusCode(500, new { Error = "Comparison failed", Message = ex.Message });
        }
    }
}
