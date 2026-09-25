using System.Collections.Generic;

namespace LaptopTracking.Api.Models.DTOs;

public class ChartDataPoint
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}

public class DashboardChartsDto
{
    public List<ChartDataPoint> AllocationStatus { get; set; } = [];
    public List<ChartDataPoint> StockAgeing { get; set; } = [];
    public List<ChartDataPoint> RasStatus { get; set; } = [];
    public List<ChartDataPoint> LwdRisk { get; set; } = [];
    public List<ChartDataPoint> LocationDistribution { get; set; } = [];
    public List<ChartDataPoint> RasIdleLocationDistribution { get; set; } = [];
}
