using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaptopTracking.Api.Data;
using LaptopTracking.Api.Models.DTOs;
using LaptopTracking.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LaptopTracking.Api.Services;

public class RasReconciliationService
{
    private readonly LaptopDbContext _db;

    public RasReconciliationService(LaptopDbContext db)
    {
        _db = db;
    }

    public async Task<ImportResultDto> ConfirmRasBatchAsync(int batchId)
    {
        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.BatchId == batchId);
        if (batch == null)
        {
            return new ImportResultDto { Success = false, Message = $"RAS Batch #{batchId} not found." };
        }

        if (batch.Status == "COMMITTED" || batch.Status == "CONFIRMED")
        {
            return new ImportResultDto { Success = false, Message = $"RAS Batch #{batchId} is already committed." };
        }

        var stagedRasRows = await _db.RASHistoricals
            .Where(r => r.BatchId == batchId)
            .OrderBy(r => r.HistoryId)
            .ToListAsync();

        if (stagedRasRows.Count == 0)
        {
            return new ImportResultDto { Success = false, Message = $"No staged RAS records found for batch #{batchId}." };
        }

        // Determine if this is an INITIAL baseline commit or SUBSEQUENT reconciliation
        bool isInitialMode = batch.ImportMode == "INITIAL" || !await _db.RASCurrents.AnyAsync();

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var incomingSapSet = new HashSet<string>(stagedRasRows.Select(r => r.EmployeeCode), StringComparer.OrdinalIgnoreCase);

            if (isInitialMode)
            {
                // ==========================================
                // 1. INITIAL BASELINE COMMIT FLOW
                // ==========================================
                // Overwrite RASCurrent with the initial baseline workforce (all valid rows preserved)
                _db.RASCurrents.RemoveRange(_db.RASCurrents);
                await _db.SaveChangesAsync();

                var initialCurrentRas = stagedRasRows.Select(MapHistoricalToCurrent).ToList();

                await _db.RASCurrents.AddRangeAsync(initialCurrentRas);
                await _db.SaveChangesAsync();

                // Establish IsLost & RASStatus for all allocated laptops
                var allocatedLaptops = await _db.LaptopCurrents
                    .Where(l => l.Status == "Allocated" || l.Status == "ALLOCATED")
                    .ToListAsync();

                int activeCount = 0;
                int lostCount = 0;

                foreach (var lap in allocatedLaptops)
                {
                    if (!string.IsNullOrEmpty(lap.SAPId) && incomingSapSet.Contains(lap.SAPId))
                    {
                        lap.IsLost = false;
                        lap.RASStatus = "ACTIVE";
                        activeCount++;
                    }
                    else
                    {
                        lap.IsLost = true;
                        lap.RASStatus = "LOST";
                        lostCount++;
                    }
                    lap.UpdatedAt = DateTimeOffset.UtcNow;
                }

                batch.Status = "COMMITTED";
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return new ImportResultDto
                {
                    Success = true,
                    Message = $"Initial RAS Baseline committed successfully. {stagedRasRows.Count} RAS records ({incomingSapSet.Count} distinct employees) established as accepted baseline. {activeCount} laptops marked Active, {lostCount} laptops marked Lost.",
                    BatchId = batch.BatchId,
                    SnapshotDate = batch.SnapshotDate,
                    RecordCount = batch.RecordCount,
                    NewCount = incomingSapSet.Count,
                    ChangedCount = 0,
                    UnchangedCount = 0,
                    MissingCount = 0
                };
            }
            else
            {
                // ==========================================
                // 2. SUBSEQUENT RECONCILIATION COMMIT FLOW
                // ==========================================
                var previousRasSaps = await _db.RASCurrents.Select(r => r.EmployeeCode).Distinct().ToListAsync();
                var prevSet = new HashSet<string>(previousRasSaps, StringComparer.OrdinalIgnoreCase);

                // Overwrite RASCurrent to represent latest accepted workforce (all valid rows preserved)
                _db.RASCurrents.RemoveRange(_db.RASCurrents);
                await _db.SaveChangesAsync();

                var newCurrentRas = stagedRasRows.Select(MapHistoricalToCurrent).ToList();

                await _db.RASCurrents.AddRangeAsync(newCurrentRas);
                await _db.SaveChangesAsync();

                // Reconcile Allocated Laptops:
                var allocatedLaptops = await _db.LaptopCurrents
                    .Where(l => (l.Status == "Allocated" || l.Status == "ALLOCATED") && l.SAPId != null)
                    .ToListAsync();

                var auditEntries = new List<LaptopAuditHistory>();
                int notInUhgCount = 0;
                int returnedCount = 0;

                foreach (var lap in allocatedLaptops)
                {
                    if (lap.IsLost)
                    {
                        // Stays Lost permanently even if employee reappears
                        lap.RASStatus = "LOST";
                        continue;
                    }

                    bool wasInPrevRas = prevSet.Contains(lap.SAPId!);
                    bool inNewRas = incomingSapSet.Contains(lap.SAPId!);

                    if (wasInPrevRas && !inNewRas)
                    {
                        // Transition to NOT_IN_UHG. Remains Status = ALLOCATED, never move to In Stock, do not clear SAP or UserName.
                        if (lap.RASStatus != "NOT_IN_UHG")
                        {
                            auditEntries.Add(new LaptopAuditHistory
                            {
                                SerialNumber = lap.SerialNumber,
                                BatchId = batch.BatchId,
                                SnapshotDate = batch.SnapshotDate,
                                FileName = batch.FileName,
                                ChangeType = "RAS_NOT_IN_UHG",
                                ChangedFields = "Employee Exited from RAS Roster (Not in UHG)",
                                PreviousFBRRequest = lap.FBRRequest,
                                PreviousLocation = lap.Location,
                                PreviousStatus = lap.Status,
                                PreviousITSPOC = lap.ITSPOC,
                                PreviousSAPId = lap.SAPId,
                                PreviousUserName = lap.UserName,
                                PreviousInStockSince = lap.InStockSince,
                                RecordedAt = DateTimeOffset.UtcNow
                            });
                            notInUhgCount++;
                        }

                        lap.RASStatus = "NOT_IN_UHG";
                        lap.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                    else if (inNewRas)
                    {
                        // Returns or continues as Active
                        if (lap.RASStatus == "NOT_IN_UHG")
                        {
                            auditEntries.Add(new LaptopAuditHistory
                            {
                                SerialNumber = lap.SerialNumber,
                                BatchId = batch.BatchId,
                                SnapshotDate = batch.SnapshotDate,
                                FileName = batch.FileName,
                                ChangeType = "RAS_RETURNED",
                                ChangedFields = "Employee Reappeared in RAS Roster",
                                PreviousFBRRequest = lap.FBRRequest,
                                PreviousLocation = lap.Location,
                                PreviousStatus = lap.Status,
                                PreviousITSPOC = lap.ITSPOC,
                                PreviousSAPId = lap.SAPId,
                                PreviousUserName = lap.UserName,
                                PreviousInStockSince = lap.InStockSince,
                                RecordedAt = DateTimeOffset.UtcNow
                            });
                            returnedCount++;
                        }

                        lap.RASStatus = "ACTIVE";
                        lap.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                }

                if (auditEntries.Count > 0)
                {
                    await _db.LaptopAuditHistories.AddRangeAsync(auditEntries);
                }

                batch.Status = "COMMITTED";
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return new ImportResultDto
                {
                    Success = true,
                    Message = $"RAS Reconciliation Batch #{batch.BatchId} committed successfully. {notInUhgCount} laptops classified as Not in UHG, {returnedCount} reactivated.",
                    BatchId = batch.BatchId,
                    SnapshotDate = batch.SnapshotDate,
                    RecordCount = batch.RecordCount,
                    NewCount = batch.NewCount,
                    ChangedCount = notInUhgCount,
                    UnchangedCount = batch.UnchangedCount,
                    MissingCount = batch.MissingCount
                };
            }
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            batch.Status = "FAILED";
            await _db.SaveChangesAsync();
            return new ImportResultDto
            {
                Success = false,
                Message = $"Failed to commit RAS batch: {ex.Message}",
                BatchId = batch.BatchId
            };
        }
    }

    private static RASCurrent MapHistoricalToCurrent(RASHistorical r)
    {
        return new RASCurrent
        {
            EmployeeCode = r.EmployeeCode,
            EmployeeName = r.EmployeeName,
            ProjectCode = r.ProjectCode,
            ProjectName = r.ProjectName,
            WBSLevel = r.WBSLevel,
            StartDate = r.StartDate,
            EndDate = r.EndDate,
            Allocation = r.Allocation,
            Role = r.Role,
            RoleName = r.RoleName,
            CustomerCode = r.CustomerCode,
            CustomerName = r.CustomerName,
            ProjectSuperLOBCode = r.ProjectSuperLOBCode,
            ProjectSuperLOBName = r.ProjectSuperLOBName,
            ProjectLOBCode = r.ProjectLOBCode,
            ProjectLOBName = r.ProjectLOBName,
            ProjectSDUCode = r.ProjectSDUCode,
            ProjectSDUName = r.ProjectSDUName,
            ProjectDUCode = r.ProjectDUCode,
            ProjectDUName = r.ProjectDUName,
            ProjectManagerCode = r.ProjectManagerCode,
            ProjectManagerName = r.ProjectManagerName,
            ProjectTypeCode = r.ProjectTypeCode,
            ProjectTypeName = r.ProjectTypeName,
            ProjectStatus = r.ProjectStatus,
            ProjectCategory = r.ProjectCategory,
            ProjectCategoryName = r.ProjectCategoryName,
            WBSType = r.WBSType,
            CompanyCode = r.CompanyCode,
            CompanyName = r.CompanyName,
            WBSPlantCode = r.WBSPlantCode,
            WBSPlantName = r.WBSPlantName,
            WBSCode = r.WBSCode,
            BillingStatus = r.BillingStatus,
            EmployeeSuperLOBCode = r.EmployeeSuperLOBCode,
            EmployeeSuperLOBName = r.EmployeeSuperLOBName,
            EmployeeLOBCode = r.EmployeeLOBCode,
            EmployeeLOBName = r.EmployeeLOBName,
            EmployeeSDUCode = r.EmployeeSDUCode,
            EmployeeSDUName = r.EmployeeSDUName,
            EmployeeDUCode = r.EmployeeDUCode,
            EmployeeDUName = r.EmployeeDUName,
            Band = r.Band,
            SubBand = r.SubBand,
            Designation = r.Designation,
            EmployeeStatus = r.EmployeeStatus,
            ReportingManagerCode = r.ReportingManagerCode,
            ReportingManagerName = r.ReportingManagerName,
            JoiningDate = r.JoiningDate,
            PSA = r.PSA,
            EmployeePlantCode = r.EmployeePlantCode,
            EmployeePlantName = r.EmployeePlantName,
            CWLCode = r.CWLCode,
            CWLName = r.CWLName,
            JobName = r.JobName,
            JobFamilyName = r.JobFamilyName,
            RequestedBy = r.RequestedBy,
            RequestedDate = r.RequestedDate,
            StateName = r.StateName,
            CityName = r.CityName,
            ZipCode = r.ZipCode,
            WorkSiteAddress1 = r.WorkSiteAddress1,
            WorkSiteAddress2 = r.WorkSiteAddress2,
            ProjectElementDescription = r.ProjectElementDescription,
            ProgramCode = r.ProgramCode,
            DepartmentCode = r.DepartmentCode,
            BillingSystem = r.BillingSystem,
            LastWorkingDay = r.LastWorkingDay,
            BatchId = r.BatchId,
            SnapshotDate = r.SnapshotDate,
            RowFingerprint = r.RowFingerprint,
            RawRecordJson = r.RawRecordJson,
            CreatedAt = r.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
