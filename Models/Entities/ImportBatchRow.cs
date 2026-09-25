using System;

namespace LaptopTracking.Api.Models.Entities;

public class ImportBatchRow
{
    public long BatchRowId { get; set; }
    public int BatchId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string FBRRequest { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Allocated, In Stock
    public string? ITSPOC { get; set; }
    public string? SAPId { get; set; }
    public string? UserName { get; set; }
    public string RASStatus { get; set; } = string.Empty; // ACTIVE, INACTIVE, UNKNOWN
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? LastWorkingDay { get; set; }
    
    public string ValidationStatus { get; set; } = "VALID"; // VALID, ERROR
    public string? ValidationMessage { get; set; }
    public string? ChangeType { get; set; } // NEW, CHANGED, UNCHANGED, MISSING
    public string? ChangedFields { get; set; }
    
    // Navigation property
    public ImportBatch? Batch { get; set; }
}
