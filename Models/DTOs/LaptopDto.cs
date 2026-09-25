using System;
using System.Text.Json.Serialization;

namespace LaptopTracking.Api.Models.DTOs;

public class LaptopDto
{
    public string SerialNumber { get; set; } = string.Empty;
    public string FBRRequest { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Allocated, In Stock

    [JsonPropertyName("itSpoc")]
    public string? ITSPOC { get; set; }

    public string? SAPId { get; set; }
    public string? UserName { get; set; }
    public string RASStatus { get; set; } = string.Empty; // ACTIVE, LOST, NOT_IN_UHG, UNKNOWN
    public bool IsLost { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? LastWorkingDay { get; set; }
    public DateOnly? InStockSince { get; set; }
    
    // Calculated properties
    public int? AgeingDays { get; set; }
    
    public DateOnly FirstSeenDate { get; set; }
    public DateOnly LastSeenDate { get; set; }
    public int LastBatchId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
