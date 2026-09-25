using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace LaptopTracking.Api.Models.Entities;

public class RASHistorical
{
    public long HistoryId { get; set; }

    public string? EmployeeName { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;

    public string? ProjectCode { get; set; }
    public string? ProjectName { get; set; }
    public string? WBSLevel { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Allocation { get; set; }

    public string? Role { get; set; }
    public string? RoleName { get; set; }

    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }

    public string? ProjectSuperLOBCode { get; set; }
    public string? ProjectSuperLOBName { get; set; }

    public string? ProjectLOBCode { get; set; }
    public string? ProjectLOBName { get; set; }

    public string? ProjectSDUCode { get; set; }
    public string? ProjectSDUName { get; set; }

    public string? ProjectDUCode { get; set; }
    public string? ProjectDUName { get; set; }

    public string? ProjectManagerCode { get; set; }
    public string? ProjectManagerName { get; set; }

    public string? ProjectTypeCode { get; set; }
    public string? ProjectTypeName { get; set; }

    public string? ProjectStatus { get; set; }
    public string? ProjectCategory { get; set; }
    public string? ProjectCategoryName { get; set; }

    public string? WBSType { get; set; }

    public string? CompanyCode { get; set; }
    public string? CompanyName { get; set; }

    public string? WBSPlantCode { get; set; }
    public string? WBSPlantName { get; set; }

    public string? WBSCode { get; set; }

    public string? BillingStatus { get; set; }

    public string? EmployeeSuperLOBCode { get; set; }
    public string? EmployeeSuperLOBName { get; set; }

    public string? EmployeeLOBCode { get; set; }
    public string? EmployeeLOBName { get; set; }

    public string? EmployeeSDUCode { get; set; }
    public string? EmployeeSDUName { get; set; }

    public string? EmployeeDUCode { get; set; }
    public string? EmployeeDUName { get; set; }

    public string? Band { get; set; }
    public string? SubBand { get; set; }
    public string? Designation { get; set; }

    public string? EmployeeStatus { get; set; }

    public string? ReportingManagerCode { get; set; }
    public string? ReportingManagerName { get; set; }

    public DateOnly? JoiningDate { get; set; }

    public string? PSA { get; set; }

    public string? EmployeePlantCode { get; set; }
    public string? EmployeePlantName { get; set; }

    public string? CWLCode { get; set; }
    public string? CWLName { get; set; }

    public string? JobName { get; set; }
    public string? JobFamilyName { get; set; }

    public string? RequestedBy { get; set; }
    public DateTimeOffset? RequestedDate { get; set; }

    public string? StateName { get; set; }
    public string? CityName { get; set; }
    public string? ZipCode { get; set; }

    public string? WorkSiteAddress1 { get; set; }
    public string? WorkSiteAddress2 { get; set; }

    public string? ProjectElementDescription { get; set; }
    public string? ProgramCode { get; set; }
    public string? DepartmentCode { get; set; }
    public string? BillingSystem { get; set; }

    public DateOnly? LastWorkingDay { get; set; }

    // Metadata
    public long BatchId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public string? RowFingerprint { get; set; }
    public string? RawRecordJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Compatibility proxies
    [NotMapped]
    public string SAPId { get => EmployeeCode; set => EmployeeCode = value; }

    [NotMapped]
    public string? Location { get => PSA; set => PSA = value; }

    [NotMapped]
    public string RawDataJson { get => RawRecordJson ?? "{}"; set => RawRecordJson = value; }
}
