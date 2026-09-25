using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaptopTracking.Api.Data;
using LaptopTracking.Api.Models.DTOs;
using LaptopTracking.Api.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LaptopTracking.Api.Services;

public class ImportService
{
    private readonly LaptopDbContext _db;
    private readonly ExcelParserService _parser;
    private readonly ComparisonEngine _comparisonEngine;

    public ImportService(LaptopDbContext db, ExcelParserService parser, ComparisonEngine comparisonEngine)
    {
        _db = db;
        _parser = parser;
        _comparisonEngine = comparisonEngine;
    }

    public async Task<ImportPreviewDto> PreviewImportAsync(IFormFile file, DateOnly snapshotDate)
    {
        using var stream = file.OpenReadStream();
        
        // 1. Create a Staged Batch
        var batch = new ImportBatch
        {
            BatchType = "LAPTOP",
            SnapshotDate = snapshotDate,
            FileName = file.FileName,
            Status = "STAGED",
            UploadedAt = DateTimeOffset.UtcNow
        };

        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync();

        // 2. Parse and Validate Excel
        var parseResult = _parser.ParseAndValidate(stream, batch.BatchId);

        if (parseResult.Errors.Count > 0)
        {
            batch.Status = "FAILED";
            batch.ErrorCount = parseResult.Errors.Count;
            await _db.SaveChangesAsync();

            return new ImportPreviewDto
            {
                BatchId = batch.BatchId,
                SnapshotDate = snapshotDate,
                FileName = file.FileName,
                RecordCount = 0,
                Errors = parseResult.Errors
            };
        }

        // 3. Load Current Accepted Laptops Dictionary (O(1) lookup by SerialNumber)
        var currentLaptops = await _db.LaptopCurrents
            .AsNoTracking()
            .ToDictionaryAsync(x => x.SerialNumber, StringComparer.OrdinalIgnoreCase);

        // 4. Run Comparison Engine
        var comparison = _comparisonEngine.Compare(currentLaptops, parseResult.ValidRows);

        // 5. Stage Processed Rows
        await _db.ImportBatchRows.AddRangeAsync(comparison.ProcessedRows);

        // 6. Update Batch Stats
        batch.RecordCount = comparison.ProcessedRows.Count;
        batch.NewCount = comparison.NewCount;
        batch.ChangedCount = comparison.ChangedCount;
        batch.UnchangedCount = comparison.UnchangedCount;
        batch.MissingCount = comparison.MissingCount;
        await _db.SaveChangesAsync();

        return new ImportPreviewDto
        {
            BatchId = batch.BatchId,
            SnapshotDate = snapshotDate,
            FileName = file.FileName,
            RecordCount = batch.RecordCount,
            NewCount = batch.NewCount,
            ChangedCount = batch.ChangedCount,
            UnchangedCount = batch.UnchangedCount,
            MissingCount = batch.MissingCount,
            Errors = [],
            ChangePreview = comparison.PreviewItems
        };
    }

    public async Task<ImportResultDto> ConfirmImportAsync(int batchId)
    {
        var batch = await _db.ImportBatches.FindAsync(batchId);
        if (batch == null)
        {
            return new ImportResultDto { Success = false, Message = $"Batch #{batchId} not found." };
        }

        if (batch.Status == "CONFIRMED")
        {
            return new ImportResultDto { Success = false, Message = $"Batch #{batchId} has already been confirmed." };
        }

        var stagedRows = await _db.ImportBatchRows
            .Where(r => r.BatchId == batchId && r.ValidationStatus == "VALID")
            .ToListAsync();

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var incomingSerials = stagedRows.Select(r => r.SerialNumber).ToList();
            var existingLaptops = await _db.LaptopCurrents
                .Where(c => incomingSerials.Contains(c.SerialNumber))
                .ToDictionaryAsync(c => c.SerialNumber, StringComparer.OrdinalIgnoreCase);

            var existingRasSaps = await _db.RASCurrents.Select(r => r.SAPId).ToListAsync();
            var rasSapSet = new HashSet<string>(existingRasSaps, StringComparer.OrdinalIgnoreCase);
            bool hasRasBaseline = rasSapSet.Count > 0;

            var auditEntries = new List<LaptopAuditHistory>();
            var newLaptops = new List<LaptopCurrent>();

            foreach (var row in stagedRows)
            {
                bool isAllocated = row.Status == "Allocated" || row.Status == "ALLOCATED";
                bool isLost = false;
                string rasStatus = "UNKNOWN";

                if (isAllocated && hasRasBaseline)
                {
                    bool isMatch = !string.IsNullOrWhiteSpace(row.SAPId) && rasSapSet.Contains(row.SAPId);
                    isLost = !isMatch;
                    rasStatus = isMatch ? "ACTIVE" : "LOST";
                }

                if (row.ChangeType == "NEW")
                {
                    DateOnly? inStockSince = null;
                    if (row.Status == "In Stock")
                    {
                        inStockSince = batch.SnapshotDate;
                    }

                    var newLap = new LaptopCurrent
                    {
                        SerialNumber = row.SerialNumber,
                        FBRRequest = row.FBRRequest,
                        Location = row.Location,
                        Status = row.Status,
                        ITSPOC = row.ITSPOC,
                        SAPId = row.SAPId,
                        UserName = row.UserName,
                        IsLost = isLost,
                        RASStatus = rasStatus,
                        InStockSince = inStockSince,
                        FirstSeenDate = batch.SnapshotDate,
                        LastSeenDate = batch.SnapshotDate,
                        LastBatchId = batch.BatchId,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    newLaptops.Add(newLap);
                }
                else if (row.ChangeType == "CHANGED" && existingLaptops.TryGetValue(row.SerialNumber, out var current))
                {
                    // Audit: Preserve the COMPLETE previous laptop record before applying changes
                    auditEntries.Add(new LaptopAuditHistory
                    {
                        SerialNumber = current.SerialNumber,
                        BatchId = batch.BatchId,
                        SnapshotDate = batch.SnapshotDate,
                        FileName = batch.FileName,
                        ChangeType = "UPDATED",
                        ChangedFields = row.ChangedFields,
                        PreviousFBRRequest = current.FBRRequest,
                        PreviousLocation = current.Location,
                        PreviousStatus = current.Status,
                        PreviousITSPOC = current.ITSPOC,
                        PreviousSAPId = current.SAPId,
                        PreviousUserName = current.UserName,
                        PreviousInStockSince = current.InStockSince,
                        RecordedAt = DateTimeOffset.UtcNow
                    });

                    // Update stock ageing start date logic
                    if (current.Status != "In Stock" && row.Status == "In Stock")
                    {
                        current.InStockSince = batch.SnapshotDate;
                    }
                    else if (current.Status == "In Stock" && row.Status != "In Stock")
                    {
                        current.InStockSince = null;
                        if (hasRasBaseline)
                        {
                            bool isMatch = !string.IsNullOrWhiteSpace(row.SAPId) && rasSapSet.Contains(row.SAPId);
                            current.IsLost = !isMatch;
                            current.RASStatus = isMatch ? "ACTIVE" : "LOST";
                        }
                    }

                    // Update current values to new accepted state
                    current.FBRRequest = row.FBRRequest;
                    current.Location = row.Location;
                    current.Status = row.Status;
                    current.ITSPOC = row.ITSPOC;
                    current.SAPId = row.SAPId;
                    current.UserName = row.UserName;
                    current.LastSeenDate = batch.SnapshotDate;
                    current.LastBatchId = batch.BatchId;
                    current.UpdatedAt = DateTimeOffset.UtcNow;
                }
                else if (row.ChangeType == "UNCHANGED" && existingLaptops.TryGetValue(row.SerialNumber, out var unchangedLap))
                {
                    unchangedLap.LastSeenDate = batch.SnapshotDate;
                    unchangedLap.LastBatchId = batch.BatchId;
                }
            }

            if (newLaptops.Count > 0)
            {
                await _db.LaptopCurrents.AddRangeAsync(newLaptops);
            }

            if (auditEntries.Count > 0)
            {
                await _db.LaptopAuditHistories.AddRangeAsync(auditEntries);
            }

            batch.Status = "CONFIRMED";
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return new ImportResultDto
            {
                Success = true,
                Message = $"Batch #{batch.BatchId} confirmed successfully.",
                BatchId = batch.BatchId,
                SnapshotDate = batch.SnapshotDate,
                RecordCount = batch.RecordCount,
                NewCount = batch.NewCount,
                ChangedCount = batch.ChangedCount,
                UnchangedCount = batch.UnchangedCount,
                MissingCount = batch.MissingCount
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            batch.Status = "FAILED";
            await _db.SaveChangesAsync();
            return new ImportResultDto
            {
                Success = false,
                Message = $"Failed to commit batch: {ex.Message}",
                BatchId = batch.BatchId
            };
        }
    }
}
