using System;
using System.Collections.Generic;

namespace LaptopTracking.Api.Models.DTOs;

public class ValidationErrorDto
{
    public int Row { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ChangeFieldDiffDto
{
    public string Field { get; set; } = string.Empty;
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
}

public class ChangePreviewItemDto
{
    public string SerialNumber { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty; // NEW, CHANGED, UNCHANGED, MISSING
    public string ChangedFields { get; set; } = string.Empty;
    public List<ChangeFieldDiffDto> Diffs { get; set; } = [];
}

public class ImportPreviewDto
{
    public int BatchId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public int NewCount { get; set; }
    public int ChangedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int MissingCount { get; set; }
    public List<ValidationErrorDto> Errors { get; set; } = [];
    public List<ChangePreviewItemDto> ChangePreview { get; set; } = [];
}

public class ConfirmImportRequest
{
    public int BatchId { get; set; }
}

public class ImportResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int BatchId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int RecordCount { get; set; }
    public int NewCount { get; set; }
    public int ChangedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int MissingCount { get; set; }
}
