using System;

namespace LaptopTracking.Api.Models.DTOs;

public class RasIdleEmployeeDto
{
    public string SAPId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty; // Maps to PSA
    public string? EmployeeStatus { get; set; }
    public DateOnly? LastWorkingDay { get; set; }
    public DateOnly SnapshotDate { get; set; }
}
