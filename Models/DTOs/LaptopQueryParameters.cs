using System;

namespace LaptopTracking.Api.Models.DTOs;

public class LaptopQueryParameters
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? AllocationStatus { get; set; } // Backward compatibility
    public string? RASStatus { get; set; }
    public bool? IsLost { get; set; }
    public string? Location { get; set; }
    public string? FBRRequest { get; set; }
    public string? FBRId { get; set; } // Backward compatibility
    public string? ITSPOC { get; set; }
    public string? SAPId { get; set; }
    public DateOnly? StartDateFrom { get; set; }
    public DateOnly? StartDateTo { get; set; }
    public DateOnly? EndDateFrom { get; set; }
    public DateOnly? EndDateTo { get; set; }
    public DateOnly? LwdFrom { get; set; }
    public DateOnly? LwdTo { get; set; }
    public int? StockAgeMin { get; set; }
    public int? StockAgeMax { get; set; }
    public bool? LwdApproaching15Days { get; set; }
    public int? BatchId { get; set; }
    public DateOnly? SnapshotDate { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? SortBy { get; set; } = "SerialNumber";
    public string? SortDirection { get; set; } = "asc";
}
