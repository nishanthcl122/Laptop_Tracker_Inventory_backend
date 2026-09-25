using System;
using System.Collections.Generic;
using LaptopTracking.Api.Models.DTOs;
using LaptopTracking.Api.Models.Entities;

namespace LaptopTracking.Api.Services;

public class ComparisonResult
{
    public int NewCount { get; set; }
    public int ChangedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int MissingCount { get; set; }
    public List<ChangePreviewItemDto> PreviewItems { get; set; } = [];
    public List<ImportBatchRow> ProcessedRows { get; set; } = [];
    public List<string> MissingSerials { get; set; } = [];
}

public class ComparisonEngine
{
    public ComparisonResult Compare(
        Dictionary<string, LaptopCurrent> currentBySerial,
        List<ImportBatchRow> incomingRows)
    {
        var result = new ComparisonResult();
        var incomingBySerial = new Dictionary<string, ImportBatchRow>(StringComparer.OrdinalIgnoreCase);

        bool isInitialBaseline = currentBySerial.Count == 0;

        foreach (var row in incomingRows)
        {
            incomingBySerial[row.SerialNumber] = row;

            if (isInitialBaseline)
            {
                row.ChangeType = "NEW";
                row.ChangedFields = null;
                result.NewCount++;
                result.ProcessedRows.Add(row);
                
                if (result.PreviewItems.Count < 100)
                {
                    result.PreviewItems.Add(new ChangePreviewItemDto
                    {
                        SerialNumber = row.SerialNumber,
                        ChangeType = "NEW",
                        ChangedFields = "Initial Baseline Entry",
                        Diffs = []
                    });
                }
                continue;
            }

            if (!currentBySerial.TryGetValue(row.SerialNumber, out var current))
            {
                // New laptop not seen in current data
                row.ChangeType = "NEW";
                row.ChangedFields = null;
                result.NewCount++;
                result.ProcessedRows.Add(row);

                if (result.PreviewItems.Count < 100)
                {
                    result.PreviewItems.Add(new ChangePreviewItemDto
                    {
                        SerialNumber = row.SerialNumber,
                        ChangeType = "NEW",
                        ChangedFields = "New Asset",
                        Diffs = []
                    });
                }
            }
            else
            {
                // Compare all 10 tracked fields
                var diffs = DetectDiffs(current, row);
                if (diffs.Count == 0)
                {
                    row.ChangeType = "UNCHANGED";
                    row.ChangedFields = null;
                    result.UnchangedCount++;
                    result.ProcessedRows.Add(row);
                }
                else
                {
                    row.ChangeType = "CHANGED";
                    var changedFieldNames = string.Join(", ", diffs.ConvertAll(d => d.Field));
                    row.ChangedFields = changedFieldNames;
                    result.ChangedCount++;
                    result.ProcessedRows.Add(row);

                    result.PreviewItems.Add(new ChangePreviewItemDto
                    {
                        SerialNumber = row.SerialNumber,
                        ChangeType = "CHANGED",
                        ChangedFields = changedFieldNames,
                        Diffs = diffs
                    });
                }
            }
        }

        // Check for missing current serials (only if not initial baseline)
        if (!isInitialBaseline)
        {
            foreach (var kvp in currentBySerial)
            {
                if (!incomingBySerial.ContainsKey(kvp.Key))
                {
                    result.MissingCount++;
                    result.MissingSerials.Add(kvp.Key);

                    if (result.PreviewItems.Count < 150)
                    {
                        result.PreviewItems.Add(new ChangePreviewItemDto
                        {
                            SerialNumber = kvp.Key,
                            ChangeType = "MISSING",
                            ChangedFields = "Not in incoming file (Retained in Current Data)",
                            Diffs = []
                        });
                    }
                }
            }
        }

        return result;
    }

    public List<ChangeFieldDiffDto> DetectDiffs(LaptopCurrent current, ImportBatchRow incoming)
    {
        var diffs = new List<ChangeFieldDiffDto>();

        if (!StringEquals(current.FBRRequest, incoming.FBRRequest))
        {
            diffs.Add(new ChangeFieldDiffDto
            {
                Field = "FBR Request",
                Before = current.FBRRequest,
                After = incoming.FBRRequest
            });
        }

        if (!StringEquals(current.Location, incoming.Location))
        {
            diffs.Add(new ChangeFieldDiffDto
            {
                Field = "Location",
                Before = current.Location,
                After = incoming.Location
            });
        }

        if (!StringEquals(current.Status, incoming.Status))
        {
            diffs.Add(new ChangeFieldDiffDto
            {
                Field = "Status",
                Before = current.Status,
                After = incoming.Status
            });
        }

        if (!StringEquals(current.ITSPOC, incoming.ITSPOC))
        {
            diffs.Add(new ChangeFieldDiffDto
            {
                Field = "IT SPOC",
                Before = current.ITSPOC ?? "—",
                After = incoming.ITSPOC ?? "—"
            });
        }

        if (!StringEquals(current.SAPId, incoming.SAPId))
        {
            diffs.Add(new ChangeFieldDiffDto
            {
                Field = "SAP ID",
                Before = current.SAPId ?? "—",
                After = incoming.SAPId ?? "—"
            });
        }

        if (!StringEquals(current.UserName, incoming.UserName))
        {
            diffs.Add(new ChangeFieldDiffDto
            {
                Field = "User Name",
                Before = current.UserName ?? "—",
                After = incoming.UserName ?? "—"
            });
        }

        return diffs;
    }

    private static bool StringEquals(string? a, string? b)
    {
        string normA = a?.Trim() ?? string.Empty;
        string normB = b?.Trim() ?? string.Empty;
        return string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase);
    }
}
