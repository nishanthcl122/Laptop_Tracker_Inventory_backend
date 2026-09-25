using System;

namespace LaptopTracking.Api.Models.Entities;

public class LaptopCurrent
{
    public string SerialNumber { get; set; } = string.Empty;
    public string FBRRequest { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = "In Stock"; // Allocated, In Stock
    public string? ITSPOC { get; set; }
    public string? SAPId { get; set; }
    public string? UserName { get; set; }
    
    // Persistent Lost flag & RAS Classification
    public bool IsLost { get; set; } = false;
    public string? RASStatus { get; set; } // ACTIVE, LOST, NOT_IN_UHG, UNKNOWN

    // Derived tracking / Stock period tracking
    public DateOnly? InStockSince { get; set; }
    public DateOnly FirstSeenDate { get; set; }
    public DateOnly LastSeenDate { get; set; }
    public int LastBatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
