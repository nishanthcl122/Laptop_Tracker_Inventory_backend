using System.Threading.Tasks;
using LaptopTracking.Api.Models.DTOs;
using LaptopTracking.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LaptopTracking.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboardService;
    private readonly LaptopService _laptopService;

    public DashboardController(DashboardService dashboardService, LaptopService laptopService)
    {
        _dashboardService = dashboardService;
        _laptopService = laptopService;
    }

    [HttpGet("laptops")]
    public async Task<IActionResult> GetLaptops([FromQuery] LaptopQueryParameters query)
    {
        var result = await _laptopService.GetLaptopsAsync(query);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var summary = await _dashboardService.GetSummaryAsync();
        return Ok(summary);
    }

    [HttpGet("charts")]
    public async Task<IActionResult> GetCharts()
    {
        var charts = await _dashboardService.GetChartsAsync();
        return Ok(charts);
    }

    [HttpGet("ras-idle-employees")]
    public async Task<IActionResult> GetRasIdleEmployees(
        [FromQuery] string? location,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _dashboardService.GetRasIdleEmployeesAsync(location, search, page, pageSize);
        return Ok(result);
    }

    [HttpGet("laptops-by-location")]
    public async Task<IActionResult> GetLaptopsByLocation([FromQuery] string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return BadRequest("Location is required.");
        }

        var result = await _dashboardService.GetLaptopsByLocationAsync(location);
        return Ok(result);
    }
}
