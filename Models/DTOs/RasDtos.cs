using System;
using System.Collections.Generic;

namespace LaptopTracking.Api.Models.DTOs;

public class RasPreviewDto
{
    public int BatchId { get; set; }
    public string BatchType { get; set; } = "RAS";
    public string Mode { get; set; } = "INITIAL"; // "INITIAL" | "RECONCILIATION"
    public bool PreviousBaselineExists { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int ColumnCount { get; set; }
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int ExactDuplicateRows { get; set; }
    public int InvalidRows { get; set; }
    public int DistinctEmployees { get; set; }
    public int BaselineEmployees { get; set; }
    public int NewEmployeesCount { get; set; }
    public int MissingEmployeesCount { get; set; }
    public int ExitedEmployees { get; set; }
    public int AffectedLaptopsCount { get; set; }
    public int LaptopsToInStock { get; set; }
    public int TotalRecords { get; set; }
    public List<AffectedLaptopDto> AffectedLaptops { get; set; } = [];
    public List<string> ColumnsDetected { get; set; } = [];
    public Dictionary<string, string> DetectedKeyColumns { get; set; } = [];
    public List<RasRowPreviewItemDto> PreviewRows { get; set; } = [];
    public List<ValidationErrorDto> Errors { get; set; } = [];
}

public class RasRowPreviewItemDto
{
    public int RowNumber { get; set; }
    public string SAPId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? LastWorkingDay { get; set; }
    public string RawDataJson { get; set; } = "{}";
}

public class AffectedLaptopDto
{
    public string SerialNumber { get; set; } = string.Empty;
    public string FBRRequest { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string? SAPId { get; set; }
    public string? UserName { get; set; }
    public string? ITSPOC { get; set; }
    public DateOnly? LastWorkingDay { get; set; }
    public string TransitionReason { get; set; } = string.Empty; // RAS_INACTIVE, LWD_PASSED, RAS_INACTIVE_AND_LWD_PASSED
}

public class RasEmployeeDto
{
    public long RASRecordId { get; set; }
    public string SAPId { get; set; } = string.Empty;
    public string EmployeeCode => SAPId;
    public string? EmployeeName { get; set; }
    public string? Location { get; set; }
    public string? PSA => Location;
    public string? EmployeeStatus { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? LastWorkingDay { get; set; }
    public long BatchId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public string RawDataJson { get; set; } = "{}";
}

public class RasQueryParameters
{
    public string? Search { get; set; }
    public string? Location { get; set; }
    public string? EmployeeStatus { get; set; }
    public string? SapId { get; set; }
    public bool? HasLwd { get; set; }
    public string? SortBy { get; set; } = "sapid";
    public string? SortDirection { get; set; } = "asc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
