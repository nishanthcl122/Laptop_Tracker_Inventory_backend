using System.Threading.Tasks;
using LaptopTracking.Api.Models.DTOs;
using LaptopTracking.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LaptopTracking.Api.Controllers;

[ApiController]
[Route("api/laptops")]
public class LaptopController : ControllerBase
{
    private readonly LaptopService _laptopService;

    public LaptopController(LaptopService laptopService)
    {
        _laptopService = laptopService;
    }

    [HttpGet]
    public async Task<IActionResult> GetLaptops([FromQuery] LaptopQueryParameters query)
    {
        var result = await _laptopService.GetLaptopsAsync(query);
        return Ok(result);
    }

    [HttpGet("{serialNumber}")]
    public async Task<IActionResult> GetLaptop(string serialNumber)
    {
        var laptop = await _laptopService.GetLaptopBySerialAsync(serialNumber);
        if (laptop == null)
        {
            return NotFound(new { success = false, message = $"Laptop '{serialNumber}' not found." });
        }
        return Ok(laptop);
    }

    [HttpGet("{serialNumber}/audit")]
    public async Task<IActionResult> GetAuditHistory(string serialNumber)
    {
        var audits = await _laptopService.GetLaptopAuditHistoryAsync(serialNumber);
        return Ok(audits);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] LaptopQueryParameters query)
    {
        var excelBytes = await _laptopService.ExportLaptopsToExcelAsync(query);
        return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"laptops_export_{System.DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }
}
