using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaptopTracking.Api.Data;
using LaptopTracking.Api.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace LaptopTracking.Api.Services;

public class DashboardService
{
    private readonly LaptopDbContext _db;

    public DashboardService(LaptopDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var maxLwd = today.AddDays(15);

        var total = await _db.LaptopCurrents.CountAsync();
        var allocated = await _db.LaptopCurrents.CountAsync(l => l.Status == "Allocated" || l.Status == "ALLOCATED");
        var inStock = await _db.LaptopCurrents.CountAsync(l => l.Status == "In Stock" || l.Status == "IN_STOCK");

        var rasActive = await _db.LaptopCurrents.CountAsync(l =>
            (l.Status == "Allocated" || l.Status == "ALLOCATED") && !l.IsLost && l.RASStatus == "ACTIVE");

        var lost = await _db.LaptopCurrents.CountAsync(l =>
            (l.Status == "Allocated" || l.Status == "ALLOCATED") && l.IsLost);

        var notInUhg = await _db.LaptopCurrents.CountAsync(l =>
            (l.Status == "Allocated" || l.Status == "ALLOCATED") && !l.IsLost && l.RASStatus == "NOT_IN_UHG");

        var lwdApproaching = await _db.LaptopCurrents.CountAsync(l =>
            (l.Status == "Allocated" || l.Status == "ALLOCATED") &&
            l.SAPId != null &&
            _db.RASCurrents.Any(r => r.EmployeeCode == l.SAPId &&
                                     r.LastWorkingDay.HasValue &&
                                     r.LastWorkingDay.Value >= today &&
                                     r.LastWorkingDay.Value <= maxLwd));

        // Distinct employees in RASCurrent with no allocated laptop
        var rasIdle = await _db.RASCurrents
            .Where(r => !_db.LaptopCurrents.Any(l => l.SAPId == r.EmployeeCode && (l.Status == "Allocated" || l.Status == "ALLOCATED")))
            .Select(r => r.EmployeeCode)
            .Distinct()
            .CountAsync();

        return new DashboardSummaryDto
        {
            TotalLaptops = total,
            AllocatedLaptops = allocated,
            InStock = inStock,
            RasActive = rasActive,
            Lost = lost,
            NotInUhg = notInUhg,
            RasIdle = rasIdle,
            LwdApproaching15Days = lwdApproaching
        };
    }

    public async Task<DashboardChartsDto> GetChartsAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var allocatedCount = await _db.LaptopCurrents.CountAsync(l => l.Status == "Allocated" || l.Status == "ALLOCATED");
        var inStockCount = await _db.LaptopCurrents.CountAsync(l => l.Status == "In Stock" || l.Status == "IN_STOCK");

        var rasActive = await _db.LaptopCurrents.CountAsync(l =>
            (l.Status == "Allocated" || l.Status == "ALLOCATED") && !l.IsLost && l.RASStatus == "ACTIVE");

        var lostCount = await _db.LaptopCurrents.CountAsync(l =>
            (l.Status == "Allocated" || l.Status == "ALLOCATED") && l.IsLost);

        var notInUhgCount = await _db.LaptopCurrents.CountAsync(l =>
            (l.Status == "Allocated" || l.Status == "ALLOCATED") && !l.IsLost && l.RASStatus == "NOT_IN_UHG");

        // 1. Allocation Status (Allocated, In Stock, Lost)
        var allocationList = new List<ChartDataPoint>
        {
            new() { Name = "Allocated", Value = allocatedCount },
            new() { Name = "In Stock", Value = inStockCount },
            new() { Name = "Lost", Value = lostCount }
        };

        // 2. Stock Ageing (In Stock only)
        var inStockLaptops = await _db.LaptopCurrents
            .Where(l => l.Status == "In Stock" || l.Status == "IN_STOCK")
            .Select(l => l.InStockSince)
            .ToListAsync();

        int age0_30 = 0, age31_60 = 0, age61_90 = 0, age90Plus = 0;
        foreach (var inStockSince in inStockLaptops)
        {
            int days = inStockSince.HasValue ? (today.DayNumber - inStockSince.Value.DayNumber) : 0;
            if (days < 0) days = 0;
            if (days <= 30) age0_30++;
            else if (days <= 60) age31_60++;
            else if (days <= 90) age61_90++;
            else age90Plus++;
        }

        var stockAgeingList = new List<ChartDataPoint>
        {
            new() { Name = "0–30 days", Value = age0_30 },
            new() { Name = "31–60 days", Value = age31_60 },
            new() { Name = "61–90 days", Value = age61_90 },
            new() { Name = "> 90 days", Value = age90Plus }
        };

        // 3. Project RAS Activity (Allocated only: ACTIVE, LOST, NOT IN UHG)
        var rasGroups = new List<ChartDataPoint>
        {
            new() { Name = "ACTIVE", Value = rasActive },
            new() { Name = "LOST", Value = lostCount },
            new() { Name = "NOT IN UHG", Value = notInUhgCount }
        };

        // 4. LWD Risk Timeline (Allocated only, sourced from RASCurrent)
        var allocatedLwds = await _db.LaptopCurrents
            .Where(l => l.Status == "Allocated" && l.SAPId != null)
            .Join(_db.RASCurrents.Where(r => r.LastWorkingDay.HasValue),
                  l => l.SAPId,
                  r => r.EmployeeCode,
                  (l, r) => r.LastWorkingDay!.Value)
            .Distinct()
            .ToListAsync();

        int overdue = 0, horizon0_7 = 0, horizon8_15 = 0, horizon15Plus = 0;
        foreach (var lwd in allocatedLwds)
        {
            int diff = lwd.DayNumber - today.DayNumber;
            if (diff < 0) overdue++;
            else if (diff <= 7) horizon0_7++;
            else if (diff <= 15) horizon8_15++;
            else horizon15Plus++;
        }

        var lwdList = new List<ChartDataPoint>
        {
            new() { Name = "Overdue", Value = overdue },
            new() { Name = "0–7 days", Value = horizon0_7 },
            new() { Name = "8–15 days", Value = horizon8_15 },
            new() { Name = "> 15 days", Value = horizon15Plus }
        };

        // 5. Laptop Count by Location (All Laptops)
        var locationList = await _db.LaptopCurrents
            .Where(l => !string.IsNullOrEmpty(l.Location))
            .GroupBy(l => l.Location)
            .Select(g => new ChartDataPoint { Name = g.Key ?? "Unknown", Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToListAsync();

        // 6. RAS Idle Location Distribution: Group by PSA, COUNT(DISTINCT EmployeeCode)
        var rasIdleLocationList = await _db.RASCurrents
            .Where(r => !_db.LaptopCurrents.Any(l => l.SAPId == r.EmployeeCode && (l.Status == "Allocated" || l.Status == "ALLOCATED")) && !string.IsNullOrEmpty(r.PSA))
            .GroupBy(r => r.PSA)
            .Select(g => new ChartDataPoint { Name = g.Key ?? "Unknown", Value = g.Select(x => x.EmployeeCode).Distinct().Count() })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToListAsync();

        return new DashboardChartsDto
        {
            AllocationStatus = allocationList,
            StockAgeing = stockAgeingList,
            RasStatus = rasGroups,
            LwdRisk = lwdList,
            LocationDistribution = locationList,
            RasIdleLocationDistribution = rasIdleLocationList
        };
    }

    public async Task<PagedResult<RasIdleEmployeeDto>> GetRasIdleEmployeesAsync(string? location, string? search, int page = 1, int pageSize = 50)
    {
        var baseQuery = _db.RASCurrents.Where(r =>
            !_db.LaptopCurrents.Any(l => l.SAPId == r.EmployeeCode && (l.Status == "Allocated" || l.Status == "ALLOCATED")));

        if (!string.IsNullOrWhiteSpace(location) && location != "ALL")
        {
            baseQuery = baseQuery.Where(r => r.PSA == location);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            baseQuery = baseQuery.Where(r => r.EmployeeCode.ToLower().Contains(s) ||
                             (r.EmployeeName != null && r.EmployeeName.ToLower().Contains(s)) ||
                             (r.PSA != null && r.PSA.ToLower().Contains(s)));
        }

        var q = baseQuery
            .GroupBy(r => r.EmployeeCode)
            .Select(g => new RasIdleEmployeeDto
            {
                SAPId = g.Key,
                EmployeeName = g.Select(x => x.EmployeeName).FirstOrDefault(x => x != null) ?? "—",
                Location = g.Select(x => x.PSA).FirstOrDefault(x => x != null) ?? "—",
                EmployeeStatus = g.Select(x => x.EmployeeStatus).FirstOrDefault(x => x != null) ?? "—",
                LastWorkingDay = g.Max(x => x.LastWorkingDay),
                SnapshotDate = g.Max(x => x.SnapshotDate)
            });

        int totalCount = await q.CountAsync();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var items = await q
            .OrderBy(r => r.Location)
            .ThenBy(r => r.EmployeeName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<RasIdleEmployeeDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<LocationLaptopsResultDto> GetLaptopsByLocationAsync(string location)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var laptops = await _db.LaptopCurrents
            .Where(l => l.Location == location)
            .OrderBy(l => l.Status)
            .ThenBy(l => l.SerialNumber)
            .ToListAsync();

        var saps = laptops.Where(l => !string.IsNullOrEmpty(l.SAPId)).Select(l => l.SAPId!).Distinct().ToList();
        var rasRows = await _db.RASCurrents
            .AsNoTracking()
            .Where(r => saps.Contains(r.EmployeeCode))
            .OrderBy(r => r.RasRecordId)
            .ToListAsync();

        var rasFirstBySap = rasRows
            .GroupBy(r => r.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rasLwdBySap = rasRows
            .Where(r => r.LastWorkingDay.HasValue)
            .GroupBy(r => r.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(r => r.LastWorkingDay)!.Value, StringComparer.OrdinalIgnoreCase);

        var dtos = laptops.Select(l => LaptopService.MapToDto(l, today, rasFirstBySap, rasLwdBySap)).ToList();

        return new LocationLaptopsResultDto
        {
            Location = location,
            TotalCount = dtos.Count,
            AllocatedCount = dtos.Count(x => x.Status == "Allocated"),
            InStockCount = dtos.Count(x => x.Status == "In Stock"),
            Laptops = dtos
        };
    }
}
