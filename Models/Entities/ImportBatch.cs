using System;
using System.Collections.Generic;

namespace LaptopTracking.Api.Models.Entities;

public class ImportBatch
{
    public int BatchId { get; set; }
    public string BatchType { get; set; } = "LAPTOP"; // LAPTOP, RAS
    public DateOnly SnapshotDate { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public int NewCount { get; set; }
    public int ChangedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int MissingCount { get; set; }
    public int ErrorCount { get; set; }
    public string Status { get; set; } = "STAGED"; // STAGED, CONFIRMED, COMMITTED, FAILED
    public string? ImportMode { get; set; } // INITIAL, RECONCILIATION
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ImportBatchRow> Rows { get; set; } = [];
}
