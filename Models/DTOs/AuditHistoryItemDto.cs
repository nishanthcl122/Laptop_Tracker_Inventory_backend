using System;

namespace LaptopTracking.Api.Models.DTOs;

public class AuditHistoryItemDto
{
    public long AuditId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public int BatchId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string? ChangedFields { get; set; }

    // Complete previous record attributes
    public string? PreviousFBRRequest { get; set; }
    public string? PreviousLocation { get; set; }
    public string? PreviousStatus { get; set; }
    public string? PreviousITSPOC { get; set; }
    public string? PreviousSAPId { get; set; }
    public string? PreviousUserName { get; set; }
    public string? PreviousRASStatus { get; set; }
    public DateOnly? PreviousStartDate { get; set; }
    public DateOnly? PreviousEndDate { get; set; }
    public DateOnly? PreviousLastWorkingDay { get; set; }
    public DateOnly? PreviousInStockSince { get; set; }

    public DateTimeOffset RecordedAt { get; set; }
}
