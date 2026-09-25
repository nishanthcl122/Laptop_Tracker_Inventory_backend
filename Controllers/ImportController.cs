using System;
using System.Threading.Tasks;
using LaptopTracking.Api.Models.DTOs;
using LaptopTracking.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LaptopTracking.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImportController : ControllerBase
{
    private readonly ImportService _importService;
    private readonly RasImportService _rasImportService;
    private readonly BatchService _batchService;

    public ImportController(ImportService importService, RasImportService rasImportService, BatchService batchService)
    {
        _importService = importService;
        _rasImportService = rasImportService;
        _batchService = batchService;
    }

    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Preview([FromForm] IFormFile file, [FromForm] string? snapshotDate)
    {
        var validation = ValidateUploadedFile(file);
        if (!validation.IsValid)
        {
            return BadRequest(new { success = false, message = validation.ErrorMessage });
        }

        DateOnly parsedDate;
        if (string.IsNullOrWhiteSpace(snapshotDate) || !DateOnly.TryParse(snapshotDate, out parsedDate))
        {
            parsedDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        }

        try
        {
            var preview = await _importService.PreviewImportAsync(file, parsedDate);
            return Ok(preview);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = $"Import validation failed: {ex.Message}" });
        }
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmImportRequest request)
    {
        if (request == null || request.BatchId <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid batch ID provided." });
        }

        var result = await _importService.ConfirmImportAsync(request.BatchId);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("ras/preview")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> PreviewRas([FromForm] IFormFile file, [FromForm] string? snapshotDate)
    {
        var validation = ValidateUploadedFile(file);
        if (!validation.IsValid)
        {
            return BadRequest(new { success = false, message = validation.ErrorMessage });
        }

        DateOnly parsedDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        if (!string.IsNullOrWhiteSpace(snapshotDate) && DateOnly.TryParse(snapshotDate, out var dt))
        {
            parsedDate = dt;
        }

        var preview = await _rasImportService.PreviewRasAsync(file, parsedDate);
        return Ok(preview);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateUploadedFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return (false, "Please select an Excel (.xlsx, .xls) or CSV (.csv) file to upload.");
        }

        var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".xls" && ext != ".csv")
        {
            return (false, "Invalid file format. Only Excel (.xlsx, .xls) or CSV (.csv) files are allowed.");
        }

        try
        {
            using var stream = file.OpenReadStream();
            byte[] header = new byte[8];
            int bytesRead = stream.Read(header, 0, Math.Min(8, (int)file.Length));
            if (bytesRead >= 4)
            {
                if (ext == ".xlsx")
                {
                    if (header[0] != 0x50 || header[1] != 0x4B)
                    {
                        return (false, "Invalid Excel file content. File does not match .xlsx signature.");
                    }
                }
                else if (ext == ".xls")
                {
                    if (header[0] != 0xD0 || header[1] != 0xCF)
                    {
                        return (false, "Invalid Excel file content. File does not match .xls signature.");
                    }
                }
                else if (ext == ".csv")
                {
                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (header[i] == 0x00)
                        {
                            return (false, "Invalid CSV file content. Binary data detected.");
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback to extension check if stream read fails
        }

        return (true, null);
    }

    [HttpPost("ras/{batchId:int}/confirm")]
    public async Task<IActionResult> ConfirmRas(int batchId)
    {
        if (batchId <= 0)
        {
            return BadRequest(new { success = false, message = "Valid BatchId is required." });
        }

        var result = await _rasImportService.ConfirmRasAsync(batchId);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpGet("batches")]
    public async Task<IActionResult> GetBatches()
    {
        var batches = await _batchService.GetBatchesAsync();
        return Ok(batches);
    }

    [HttpGet("batches/{batchId:int}")]
    public async Task<IActionResult> GetBatchDetails(int batchId)
    {
        var details = await _batchService.GetBatchDetailsAsync(batchId);
        if (details == null) return NotFound(new { success = false, message = $"Batch #{batchId} not found." });
        return Ok(details);
    }
}
