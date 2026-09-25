using System.Collections.Generic;

namespace LaptopTracking.Api.Models.DTOs;

public class LocationLaptopsResultDto
{
    public string Location { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int AllocatedCount { get; set; }
    public int InStockCount { get; set; }
    public List<LaptopDto> Laptops { get; set; } = new();
}
