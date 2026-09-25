using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaptopTracking.Api.Data;
using LaptopTracking.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LaptopTracking.Api.Services;

public class BatchSummaryDto
{
    public ImportBatch Batch { get; set; } = null!;
    public List<ImportBatchRow> StagedRows { get; set; } = [];
    public List<RASHistorical> StagedRasRows { get; set; } = [];
}

public class BatchService
{
    private readonly LaptopDbContext _db;

    public BatchService(LaptopDbContext db)
    {
        _db = db;
    }

    public async Task<List<ImportBatch>> GetBatchesAsync()
    {
        return await _db.ImportBatches
            .AsNoTracking()
            .OrderByDescending(b => b.SnapshotDate)
            .ThenByDescending(b => b.UploadedAt)
            .ToListAsync();
    }

    public async Task<BatchSummaryDto?> GetBatchDetailsAsync(int batchId)
    {
        var batch = await _db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.BatchId == batchId);
        if (batch == null) return null;

        var rows = await _db.ImportBatchRows
            .AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .ToListAsync();

        var rasRows = await _db.RASHistoricals
            .AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .Take(100)
            .ToListAsync();

        return new BatchSummaryDto
        {
            Batch = batch,
            StagedRows = rows,
            StagedRasRows = rasRows
        };
    }
}
