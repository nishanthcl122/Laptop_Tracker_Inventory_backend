namespace LaptopTracking.Api.Models.DTOs;

public class DashboardSummaryDto
{
    // Laptop Details Hierarchy
    public int TotalLaptops { get; set; }
    public int AllocatedLaptops { get; set; }
    public int InStock { get; set; }

    // Allocated Breakdown: Allocated = RasActive + Lost + NotInUhg
    public int RasActive { get; set; }
    public int Lost { get; set; }
    public int NotInUhg { get; set; }

    // Compatibility proxy
    public int RasInactive => Lost;

    // Numbers from RAS
    public int RasIdle { get; set; }
    public int LwdApproaching15Days { get; set; }
}
