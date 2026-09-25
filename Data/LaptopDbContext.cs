using LaptopTracking.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LaptopTracking.Api.Data;

public class LaptopDbContext : DbContext
{
    public LaptopDbContext(DbContextOptions<LaptopDbContext> options) : base(options)
    {
    }

    public DbSet<LaptopCurrent> LaptopCurrents => Set<LaptopCurrent>();
    public DbSet<LaptopAuditHistory> LaptopAuditHistories => Set<LaptopAuditHistory>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportBatchRow> ImportBatchRows => Set<ImportBatchRow>();
    public DbSet<RASCurrent> RASCurrents => Set<RASCurrent>();
    public DbSet<RASHistorical> RASHistoricals => Set<RASHistorical>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ImportBatch
        modelBuilder.Entity<ImportBatch>(entity =>
        {
            entity.HasKey(e => e.BatchId);
            entity.Property(e => e.BatchType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.FileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ImportMode).HasMaxLength(50);
            entity.HasIndex(e => e.SnapshotDate);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.BatchType);
        });

        // ImportBatchRow
        modelBuilder.Entity<ImportBatchRow>(entity =>
        {
            entity.HasKey(e => e.BatchRowId);
            entity.Property(e => e.SerialNumber).HasMaxLength(100).IsRequired();
            entity.Property(e => e.FBRRequest).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Location).HasMaxLength(150).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ITSPOC).HasMaxLength(150);
            entity.Property(e => e.SAPId).HasMaxLength(100);
            entity.Property(e => e.UserName).HasMaxLength(200);
            entity.Property(e => e.RASStatus).HasMaxLength(50);
            entity.Property(e => e.ValidationStatus).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ChangeType).HasMaxLength(50);

            entity.HasOne(e => e.Batch)
                  .WithMany(b => b.Rows)
                  .HasForeignKey(e => e.BatchId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.BatchId, e.SerialNumber });
        });

        // LaptopCurrent - Only hardware & laptop allocation state
        modelBuilder.Entity<LaptopCurrent>(entity =>
        {
            entity.HasKey(e => e.SerialNumber);
            entity.Property(e => e.SerialNumber).HasMaxLength(100);
            entity.Property(e => e.FBRRequest).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Location).HasMaxLength(150).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ITSPOC).HasMaxLength(150);
            entity.Property(e => e.SAPId).HasMaxLength(100);
            entity.Property(e => e.UserName).HasMaxLength(200);
            entity.Property(e => e.IsLost).HasDefaultValue(false);
            entity.Property(e => e.RASStatus).HasMaxLength(50);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Location);
            entity.HasIndex(e => e.FBRRequest);
            entity.HasIndex(e => e.ITSPOC);
            entity.HasIndex(e => e.SAPId);
            entity.HasIndex(e => e.InStockSince);
            entity.HasIndex(e => e.IsLost);
            entity.HasIndex(e => e.RASStatus);
        });

        // LaptopAuditHistory - Full previous row records
        modelBuilder.Entity<LaptopAuditHistory>(entity =>
        {
            entity.HasKey(e => e.AuditId);
            entity.Property(e => e.SerialNumber).HasMaxLength(100).IsRequired();
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.ChangeType).HasMaxLength(50);
            entity.Property(e => e.PreviousFBRRequest).HasMaxLength(100);
            entity.Property(e => e.PreviousLocation).HasMaxLength(150);
            entity.Property(e => e.PreviousStatus).HasMaxLength(50);
            entity.Property(e => e.PreviousITSPOC).HasMaxLength(150);
            entity.Property(e => e.PreviousSAPId).HasMaxLength(100);
            entity.Property(e => e.PreviousUserName).HasMaxLength(200);
            entity.Property(e => e.PreviousRASStatus).HasMaxLength(50);

            entity.HasIndex(e => e.SerialNumber);
            entity.HasIndex(e => e.SnapshotDate);
            entity.HasIndex(e => e.RecordedAt);
        });

        // RASCurrent - 68 actual columns
        modelBuilder.Entity<RASCurrent>(entity =>
        {
            entity.HasKey(e => e.RasRecordId);
            entity.Property(e => e.EmployeeCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.EmployeeName).HasMaxLength(200);
            entity.Property(e => e.PSA).HasMaxLength(200);
            entity.Property(e => e.RowFingerprint).HasMaxLength(128);

            // Non-unique index on EmployeeCode
            entity.HasIndex(e => e.EmployeeCode);
            entity.HasIndex(e => e.BatchId);
            entity.HasIndex(e => e.SnapshotDate);
            entity.HasIndex(e => e.PSA);
            entity.HasIndex(e => new { e.BatchId, e.RowFingerprint });
        });

        // RASHistorical - 68 actual columns
        modelBuilder.Entity<RASHistorical>(entity =>
        {
            entity.HasKey(e => e.HistoryId);
            entity.Property(e => e.EmployeeCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.EmployeeName).HasMaxLength(200);
            entity.Property(e => e.PSA).HasMaxLength(200);
            entity.Property(e => e.RowFingerprint).HasMaxLength(128);

            // Non-unique index on EmployeeCode
            entity.HasIndex(e => e.EmployeeCode);
            entity.HasIndex(e => e.BatchId);
            entity.HasIndex(e => e.SnapshotDate);
            entity.HasIndex(e => new { e.BatchId, e.RowFingerprint });
        });
    }
}
