using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Weather;
using ShelfGuard.Application.Features.Weather.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/weather")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class WeatherController : ControllerBase
{
    private readonly IWeatherService _weather;

    public WeatherController(IWeatherService weather) => _weather = weather;

    [HttpGet("{storeId:guid}")]
    [ProducesResponseType(typeof(List<WeatherDayDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForecast(Guid storeId, CancellationToken ct)
        => Ok(await _weather.GetForecastAsync(storeId, ct));

    [HttpGet("{storeId:guid}/history")]
    [ProducesResponseType(typeof(List<WeatherDayDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        Guid storeId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Ok(await _weather.GetHistoryAsync(storeId, from, to, ct));

    [HttpPost("fetch")]
    [Authorize(Policy = AppPolicies.AtLeastNetworkManager)]
    [ProducesResponseType(typeof(FetchWeatherResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Fetch(CancellationToken ct)
        => Ok(await _weather.FetchAsync(ct));

    [HttpGet("coefficients")]
    [ProducesResponseType(typeof(List<WeatherCoefficientDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCoefficients(CancellationToken ct)
        => Ok(await _weather.GetCoefficientsAsync(ct));

    [HttpPost("coefficients")]
    [ProducesResponseType(typeof(WeatherCoefficientDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCoefficient(
        CreateWeatherCoefficientRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (coef, error) = await _weather.CreateCoefficientAsync(tenantId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });
        return Created($"/api/weather/coefficients/{coef!.Id}", coef);
    }

    [HttpPut("coefficients/{id:guid}")]
    [ProducesResponseType(typeof(WeatherCoefficientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCoefficient(
        Guid id, UpdateWeatherCoefficientRequest request, CancellationToken ct)
    {
        var (coef, error) = await _weather.UpdateCoefficientAsync(id, request, ct);
        if (error == "Weather coefficient not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(coef);
    }

    private Guid? GetTenantId()
    {
        Guid.TryParse(User.FindFirstValue("tenant_id"), out var id);
        return id == Guid.Empty ? null : id;
    }
}
