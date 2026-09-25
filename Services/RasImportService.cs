using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ClosedXML.Excel;
using LaptopTracking.Api.Data;
using LaptopTracking.Api.Models.DTOs;
using LaptopTracking.Api.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LaptopTracking.Api.Services;

public class RasImportService
{
    private readonly LaptopDbContext _db;
    private readonly RasReconciliationService _reconciliation;

    public RasImportService(LaptopDbContext db, RasReconciliationService reconciliation)
    {
        _db = db;
        _reconciliation = reconciliation;
    }

    public async Task<RasPreviewDto> PreviewRasAsync(IFormFile file, DateOnly snapshotDate)
    {
        var result = new RasPreviewDto
        {
            SnapshotDate = snapshotDate,
            FileName = Path.GetFileName(file.FileName)
        };

        // 1. Detect Dual-Mode: INITIAL vs RECONCILIATION
        bool previousBaselineExists = await _db.ImportBatches.AnyAsync(b => b.BatchType == "RAS" && (b.Status == "COMMITTED" || b.Status == "CONFIRMED"))
                                      || await _db.RASCurrents.AnyAsync();

        string mode = previousBaselineExists ? "RECONCILIATION" : "INITIAL";
        result.Mode = mode;
        result.PreviousBaselineExists = previousBaselineExists;

        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);

        // 2. Identify the active worksheet with data
        IXLWorksheet? worksheet = null;
        foreach (var ws in workbook.Worksheets)
        {
            if (ws.FirstRowUsed() != null && ws.LastRowUsed() != null && ws.LastRowUsed()!.RowNumber() >= ws.FirstRowUsed()!.RowNumber())
            {
                worksheet = ws;
                break;
            }
        }

        if (worksheet == null)
        {
            result.Errors.Add(new ValidationErrorDto { Row = 1, Field = "File", Message = "Workbook contains no sheets with readable data." });
            return result;
        }

        // 3. Robust Header Row Detection (scan first 15 rows for highest header score)
        int firstRow = worksheet.FirstRowUsed()!.RowNumber();
        int lastRow = worksheet.LastRowUsed()!.RowNumber();
        IXLRow? headerRow = null;
        int maxHeaderScore = -1;

        for (int r = firstRow; r <= Math.Min(firstRow + 15, lastRow); r++)
        {
            var row = worksheet.Row(r);
            if (row.IsEmpty()) continue;

            int score = 0;
            foreach (var cell in row.CellsUsed())
            {
                string text = cell.GetString().Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (text.Contains("employee") || text.Contains("emp") || text.Contains("code") || text.Contains("sap") ||
                    text.Contains("name") || text.Contains("location") || text.Contains("psa") || text.Contains("subarea") ||
                    text.Contains("project") || text.Contains("wbs") ||
                    text.Contains("lwd") || text.Contains("date") || text.Contains("status") || text.Contains("role"))
                {
                    score++;
                }
            }

            if (score > maxHeaderScore)
            {
                maxHeaderScore = score;
                headerRow = row;
            }
        }

        headerRow ??= worksheet.FirstRowUsed();
        if (headerRow == null)
        {
            result.Errors.Add(new ValidationErrorDto { Row = 1, Field = "File", Message = "Could not identify header row in the worksheet." });
            return result;
        }

        // 4. Extract and preserve ALL actual headers
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var detectedHeaders = new List<string>();

        foreach (var cell in headerRow.CellsUsed())
        {
            string headerText = cell.GetString().Trim();
            if (!string.IsNullOrEmpty(headerText) && !colMap.ContainsKey(headerText))
            {
                colMap[headerText] = cell.Address.ColumnNumber;
                detectedHeaders.Add(headerText);
            }
        }
        result.ColumnsDetected = detectedHeaders;
        result.ColumnCount = detectedHeaders.Count;

        // 5. Identify key columns with comprehensive candidate lists
        int sapCol = FindColumn(colMap, out string? matchedSapHeader,
            "Employee Code", "EmployeeCode", "Emp Code", "EmpCode",
            "SAP ID", "Employee SAP ID", "SAPID", "SAP",
            "Employee ID", "EmployeeID", "Emp ID", "EmpID",
            "Personnel Number", "PersonnelNumber", "Staff ID", "User ID");

        int nameCol = FindColumn(colMap, out string? matchedNameHeader,
            "Employee Name", "EmployeeName", "Emp Name", "EmpName",
            "Resource Name", "ResourceName", "Full Name", "FullName",
            "Employee", "Name");

        int lwdCol = FindColumn(colMap, out string? matchedLwdHeader,
            "LastWorkingDay", "Last Working Day", "Last Working Date", "LWD",
            "Separation Date", "Relieving Date", "Exit Date", "LastDate");

        int locCol = FindColumn(colMap, out string? matchedLocHeader,
            "PSA", "PSA Description", "PSA Desc", "Personnel Subarea", "Personnel Sub Area", "Personnel Subarea Desc",
            "Base Location", "BaseLocation", "Employee Location", "EmployeeLocation",
            "Work Location", "WorkLocation", "Location", "City", "Office Location", "Branch");

        if (matchedSapHeader != null) result.DetectedKeyColumns["SAP ID / Employee Code"] = matchedSapHeader;
        if (matchedNameHeader != null) result.DetectedKeyColumns["Employee Name"] = matchedNameHeader;
        if (matchedLocHeader != null) result.DetectedKeyColumns["Location"] = matchedLocHeader;
        if (matchedLwdHeader != null) result.DetectedKeyColumns["Last Working Day"] = matchedLwdHeader;

        if (sapCol == -1)
        {
            result.Errors.Add(new ValidationErrorDto
            {
                Row = headerRow.RowNumber(),
                Field = "Header",
                Message = $"Could not find a column for 'SAP ID' or 'Employee Code'. Detected headers: {string.Join(", ", detectedHeaders.Take(8))}..."
            });
            return result;
        }

        // 6. Create Staged ImportBatch record immediately so batchId is valid!
        var batch = new ImportBatch
        {
            BatchType = "RAS",
            ImportMode = mode,
            SnapshotDate = snapshotDate,
            FileName = result.FileName,
            Status = "STAGED",
            UploadedAt = DateTimeOffset.UtcNow
        };
        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync();
        result.BatchId = batch.BatchId;

        // 7. Parse and stage actual data rows
        var incomingRas = new List<RASHistorical>();
        var seenFingerprints = new HashSet<string>();
        var distinctSapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int startRow = headerRow.RowNumber() + 1;
        int lastDataRow = worksheet.LastRowUsed()!.RowNumber();

        for (int rowNum = startRow; rowNum <= lastDataRow; rowNum++)
        {
            var row = worksheet.Row(rowNum);
            if (row.IsEmpty()) continue;

            // Check if all cells in this row are empty or whitespace
            bool hasData = false;
            foreach (var c in row.CellsUsed())
            {
                if (!string.IsNullOrWhiteSpace(c.GetString()))
                {
                    hasData = true;
                    break;
                }
            }
            if (!hasData) continue;

            // 1. Exact Full-Row Duplicate Check (Deterministic Row Fingerprint)
            string fingerprint = ComputeRowFingerprint(row, colMap);
            if (seenFingerprints.Contains(fingerprint))
            {
                result.ExactDuplicateRows++;
                if (result.Errors.Count < 20)
                {
                    result.Errors.Add(new ValidationErrorDto
                    {
                        Row = rowNum,
                        Field = "Row",
                        Message = $"Row {rowNum} is an exact full-row duplicate of an earlier record."
                    });
                }
                continue;
            }
            seenFingerprints.Add(fingerprint);

            // 2. SAP ID / Employee Code Validation
            string rawSap = GetCellString(row, sapCol);
            if (string.IsNullOrWhiteSpace(rawSap))
            {
                result.InvalidRows++;
                if (result.Errors.Count < 20)
                {
                    result.Errors.Add(new ValidationErrorDto
                    {
                        Row = rowNum,
                        Field = matchedSapHeader ?? "SAP ID",
                        Message = $"Row {rowNum}: Employee Code / SAP ID is blank."
                    });
                }
                continue;
            }

            string sapId = rawSap.Trim().ToUpperInvariant();
            distinctSapIds.Add(sapId);

            string? empName = NormalizeSpaces(GetCellString(row, nameCol));
            string? location = string.IsNullOrWhiteSpace(GetCellString(row, locCol)) ? null : GetCellString(row, locCol).Trim();

            // Capture 100% of all detected columns dynamically
            var rowDataDict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in colMap)
            {
                rowDataDict[kvp.Key] = GetCellString(row, kvp.Value);
            }
            string rawJson = JsonSerializer.Serialize(rowDataDict);

            string? psaVal = GetVal(rowDataDict, "PSA", "PSA Description", "PSA Desc", "Personnel Subarea", "Personnel Sub Area", "Personnel Subarea Desc") ?? location;
            DateOnly? startDate = GetDate(row, colMap, rowDataDict, "Start Date", "StartDate");
            DateOnly? endDate = GetDate(row, colMap, rowDataDict, "End Date", "EndDate");
            DateOnly? lwd = ParseDateCell(row, lwdCol) ?? GetDate(row, colMap, rowDataDict, "LastWorkingDay", "Last Working Day", "Last Working Date", "LWD", "Separation Date", "Relieving Date", "Exit Date", "LastDate");

            var historical = new RASHistorical
            {
                BatchId = batch.BatchId,
                SnapshotDate = snapshotDate,
                EmployeeCode = sapId,
                EmployeeName = empName,
                ProjectCode = GetVal(rowDataDict, "Project Code", "ProjectCode"),
                ProjectName = GetVal(rowDataDict, "Project Name", "ProjectName"),
                WBSLevel = GetVal(rowDataDict, "WBS Level", "WBSLevel"),
                StartDate = startDate,
                EndDate = endDate,
                Allocation = GetVal(rowDataDict, "Allocation"),
                Role = GetVal(rowDataDict, "Role"),
                RoleName = GetVal(rowDataDict, "RoleName", "Role Name"),
                CustomerCode = GetVal(rowDataDict, "Customer Code", "CustomerCode"),
                CustomerName = GetVal(rowDataDict, "Customer Name", "CustomerName"),
                ProjectSuperLOBCode = GetVal(rowDataDict, "ProjectSuperLOBCode", "Project Super LOB Code"),
                ProjectSuperLOBName = GetVal(rowDataDict, "ProjectSuperLOBName", "Project Super LOB Name"),
                ProjectLOBCode = GetVal(rowDataDict, "ProjectLOBCode", "Project LOB Code"),
                ProjectLOBName = GetVal(rowDataDict, "ProjectLOBName", "Project LOB Name"),
                ProjectSDUCode = GetVal(rowDataDict, "ProjectSDUCode", "Project SDU Code"),
                ProjectSDUName = GetVal(rowDataDict, "ProjectSDUName", "Project SDU Name"),
                ProjectDUCode = GetVal(rowDataDict, "ProjectDUCode", "Project DU Code"),
                ProjectDUName = GetVal(rowDataDict, "ProjectDUName", "Project DU Name"),
                ProjectManagerCode = GetVal(rowDataDict, "ProjectManagerCode", "Project Manager Code"),
                ProjectManagerName = GetVal(rowDataDict, "ProjectManagerName", "Project Manager Name"),
                ProjectTypeCode = GetVal(rowDataDict, "Project Type Code", "ProjectTypeCode"),
                ProjectTypeName = GetVal(rowDataDict, "Project Type Name", "ProjectTypeName"),
                ProjectStatus = GetVal(rowDataDict, "Project Status", "ProjectStatus"),
                ProjectCategory = GetVal(rowDataDict, "Project Category", "ProjectCategory"),
                ProjectCategoryName = GetVal(rowDataDict, "ProjectCategoryName", "Project Category Name"),
                WBSType = GetVal(rowDataDict, "WBS Type", "WBSType"),
                CompanyCode = GetVal(rowDataDict, "Company Code", "CompanyCode"),
                CompanyName = GetVal(rowDataDict, "Company Name", "CompanyName"),
                WBSPlantCode = GetVal(rowDataDict, "WBS Plant Code", "WBSPlantCode"),
                WBSPlantName = GetVal(rowDataDict, "WBS Plant Name", "WBSPlantName"),
                WBSCode = GetVal(rowDataDict, "wbs Code", "WBS Code", "WBSCode"),
                BillingStatus = GetVal(rowDataDict, "BillingStatus", "Billing Status"),
                EmployeeSuperLOBCode = GetVal(rowDataDict, "EmployeeSuperLOBCode", "Employee Super LOB Code"),
                EmployeeSuperLOBName = GetVal(rowDataDict, "EmployeeSuperLOBName", "Employee Super LOB Name"),
                EmployeeLOBCode = GetVal(rowDataDict, "EmployeeLOBCode", "Employee LOB Code"),
                EmployeeLOBName = GetVal(rowDataDict, "EmployeeLOBName", "Employee LOB Name"),
                EmployeeSDUCode = GetVal(rowDataDict, "EmployeeSDUCode", "Employee SDU Code"),
                EmployeeSDUName = GetVal(rowDataDict, "EmployeeSDUName", "Employee SDU Name"),
                EmployeeDUCode = GetVal(rowDataDict, "EmployeeDUCode", "Employee DU Code"),
                EmployeeDUName = GetVal(rowDataDict, "EmployeeDUName", "Employee DU Name"),
                Band = GetVal(rowDataDict, "Band"),
                SubBand = GetVal(rowDataDict, "SubBand", "Sub Band"),
                Designation = GetVal(rowDataDict, "Designation"),
                EmployeeStatus = GetVal(rowDataDict, "EmployeeStatus", "Employee Status"),
                ReportingManagerCode = GetVal(rowDataDict, "ReportingManagerCode", "Reporting Manager Code"),
                ReportingManagerName = GetVal(rowDataDict, "ReportingManagerName", "Reporting Manager Name"),
                JoiningDate = GetDate(row, colMap, rowDataDict, "JoiningDate", "Joining Date", "DOJ"),
                PSA = psaVal,
                EmployeePlantCode = GetVal(rowDataDict, "Employee Plant Code", "EmployeePlantCode"),
                EmployeePlantName = GetVal(rowDataDict, "Employee Plant Name", "EmployeePlantName"),
                CWLCode = GetVal(rowDataDict, "CWL Code", "CWLCode"),
                CWLName = GetVal(rowDataDict, "CWLName", "CWL Name"),
                JobName = GetVal(rowDataDict, "Job Name", "JobName"),
                JobFamilyName = GetVal(rowDataDict, "Job Family Name", "JobFamilyName"),
                RequestedBy = GetVal(rowDataDict, "Requested By", "RequestedBy"),
                RequestedDate = GetDateTime(row, colMap, rowDataDict, "Requested Date", "RequestedDate"),
                StateName = GetVal(rowDataDict, "State Name", "StateName"),
                CityName = GetVal(rowDataDict, "City Name", "CityName"),
                ZipCode = GetVal(rowDataDict, "ZipCode", "Zip Code"),
                WorkSiteAddress1 = GetVal(rowDataDict, "Work Site Address 1", "WorkSiteAddress1"),
                WorkSiteAddress2 = GetVal(rowDataDict, "Work Site Address 2", "WorkSiteAddress2"),
                ProjectElementDescription = GetVal(rowDataDict, "Project Element Description", "ProjectElementDescription"),
                ProgramCode = GetVal(rowDataDict, "Program Code", "ProgramCode"),
                DepartmentCode = GetVal(rowDataDict, "Department Code", "DepartmentCode"),
                BillingSystem = GetVal(rowDataDict, "Billing System", "BillingSystem"),
                LastWorkingDay = lwd,
                RowFingerprint = fingerprint,
                RawRecordJson = rawJson,
                CreatedAt = DateTimeOffset.UtcNow
            };
            incomingRas.Add(historical);

            // Add to PreviewRows (first 40 rows for interactive table)
            if (result.PreviewRows.Count < 40)
            {
                result.PreviewRows.Add(new RasRowPreviewItemDto
                {
                    RowNumber = rowNum,
                    SAPId = sapId,
                    EmployeeName = empName ?? "—",
                    Location = psaVal ?? "—",
                    StartDate = startDate,
                    EndDate = endDate,
                    LastWorkingDay = lwd,
                    RawDataJson = rawJson
                });
            }
        }

        result.ValidRows = incomingRas.Count;
        result.DistinctEmployees = distinctSapIds.Count;
        result.TotalRows = incomingRas.Count + result.ExactDuplicateRows + result.InvalidRows;
        result.TotalRecords = incomingRas.Count;
        batch.RecordCount = incomingRas.Count;
        batch.ErrorCount = result.InvalidRows + result.ExactDuplicateRows;

        // Persist staged historical rows
        await _db.RASHistoricals.AddRangeAsync(incomingRas);
        await _db.SaveChangesAsync();

        // 8. Dual-Mode Evaluation
        if (mode == "INITIAL")
        {
            result.BaselineEmployees = distinctSapIds.Count;
            result.NewEmployeesCount = distinctSapIds.Count;
            result.MissingEmployeesCount = 0;
            result.ExitedEmployees = 0;
            result.AffectedLaptopsCount = 0;
            result.LaptopsToInStock = 0;
            result.AffectedLaptops = [];

            batch.NewCount = distinctSapIds.Count;
            batch.MissingCount = 0;
            batch.ChangedCount = 0;
            batch.UnchangedCount = 0;
        }
        else
        {
            // RECONCILIATION Mode: compare distinct SAPs against previous accepted baseline
            var previousSaps = await _db.RASCurrents
                .Select(r => r.SAPId)
                .Distinct()
                .ToListAsync();
            var prevSapSet = new HashSet<string>(previousSaps, StringComparer.OrdinalIgnoreCase);
            var incomingSapSet = distinctSapIds;

            result.BaselineEmployees = incomingSapSet.Count;
            result.NewEmployeesCount = incomingSapSet.Count(s => !prevSapSet.Contains(s));
            result.MissingEmployeesCount = prevSapSet.Count(s => !incomingSapSet.Contains(s));
            result.ExitedEmployees = result.MissingEmployeesCount;

            batch.NewCount = result.NewEmployeesCount;
            batch.MissingCount = result.MissingEmployeesCount;
            batch.UnchangedCount = incomingSapSet.Count(s => prevSapSet.Contains(s));

            // Affected Allocated Laptops:
            // Only laptops whose assigned SAP was in previous RAS and is now absent from new RAS, and is not already Lost
            var prevRasLwds = await _db.RASCurrents
                .Where(r => r.LastWorkingDay.HasValue)
                .GroupBy(r => r.EmployeeCode)
                .ToDictionaryAsync(g => g.Key, g => g.Max(r => r.LastWorkingDay)!.Value, StringComparer.OrdinalIgnoreCase);

            var incomingRasLwds = incomingRas
                .Where(r => r.LastWorkingDay.HasValue)
                .GroupBy(r => r.EmployeeCode)
                .ToDictionary(g => g.Key, g => g.Max(r => r.LastWorkingDay)!.Value, StringComparer.OrdinalIgnoreCase);

            var allocatedLaptops = await _db.LaptopCurrents
                .Where(l => (l.Status == "Allocated" || l.Status == "ALLOCATED") && l.SAPId != null)
                .ToListAsync();

            var affectedList = new List<AffectedLaptopDto>();
            foreach (var lap in allocatedLaptops)
            {
                if (lap.IsLost) continue;

                bool wasInPrevRas = prevSapSet.Contains(lap.SAPId!);
                bool inNewRas = incomingSapSet.Contains(lap.SAPId!);
                DateOnly? lapLwd = incomingRasLwds.TryGetValue(lap.SAPId!, out var incLwd) ? incLwd :
                                  prevRasLwds.TryGetValue(lap.SAPId!, out var pLwd) ? pLwd : null;

                if (wasInPrevRas && !inNewRas)
                {
                    affectedList.Add(new AffectedLaptopDto
                    {
                        SerialNumber = lap.SerialNumber,
                        FBRRequest = lap.FBRRequest,
                        Location = lap.Location,
                        SAPId = lap.SAPId,
                        UserName = lap.UserName,
                        ITSPOC = lap.ITSPOC,
                        LastWorkingDay = lapLwd,
                        TransitionReason = "NOT_IN_UHG"
                    });
                }
            }

            result.AffectedLaptops = affectedList;
            result.AffectedLaptopsCount = affectedList.Count;
            result.LaptopsToInStock = 0;
            batch.ChangedCount = affectedList.Count;
        }

        await _db.SaveChangesAsync();
        return result;
    }

    public async Task<ImportResultDto> ConfirmRasAsync(int batchId)
    {
        return await _reconciliation.ConfirmRasBatchAsync(batchId);
    }

    private static int FindColumn(Dictionary<string, int> map, out string? matchedHeader, params string[] candidates)
    {
        matchedHeader = null;
        // 1. Exact match
        foreach (var c in candidates)
        {
            if (map.TryGetValue(c, out int col))
            {
                matchedHeader = c;
                return col;
            }
        }

        // 2. Normalized alphanumeric match (spaces/punctuation stripped)
        foreach (var kvp in map)
        {
            string cleanKvp = Regex.Replace(kvp.Key, @"[^a-zA-Z0-9]", "");
            foreach (var c in candidates)
            {
                string cleanC = Regex.Replace(c, @"[^a-zA-Z0-9]", "");
                if (cleanKvp.Equals(cleanC, StringComparison.OrdinalIgnoreCase))
                {
                    matchedHeader = kvp.Key;
                    return kvp.Value;
                }
            }
        }

        // 3. Substring / contains match
        foreach (var kvp in map)
        {
            string cleanKvp = Regex.Replace(kvp.Key, @"[^a-zA-Z0-9]", "").ToLowerInvariant();
            foreach (var c in candidates)
            {
                string cleanC = Regex.Replace(c, @"[^a-zA-Z0-9]", "").ToLowerInvariant();
                if (cleanKvp.Contains(cleanC) || cleanC.Contains(cleanKvp))
                {
                    matchedHeader = kvp.Key;
                    return kvp.Value;
                }
            }
        }

        return -1;
    }

    private static string GetCellString(IXLRow row, int colNumber)
    {
        if (colNumber <= 0) return string.Empty;
        var cell = row.Cell(colNumber);
        return cell.GetString().Trim();
    }

    private static string? NormalizeSpaces(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Regex.Replace(raw.Trim(), @"\s+", " ");
    }

    private static DateOnly? ParseDateCell(IXLRow row, int colNumber)
    {
        if (colNumber <= 0) return null;
        var cell = row.Cell(colNumber);
        if (cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.DateTime)
        {
            return DateOnly.FromDateTime(cell.GetDateTime().Date);
        }

        if (cell.DataType == XLDataType.Number)
        {
            try
            {
                double num = cell.GetDouble();
                var dt = DateTime.FromOADate(num);
                return DateOnly.FromDateTime(dt.Date);
            }
            catch { }
        }

        string text = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;

        string[] formats = ["yyyy-MM-dd", "dd-MMM-yyyy", "dd-MM-yyyy", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy/MM/dd", "d-MMM-yy", "d-MMM-yyyy", "yyyy-MM-dd HH:mm:ss", "MM/dd/yyyy HH:mm:ss"];
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedExact))
        {
            return DateOnly.FromDateTime(parsedExact.Date);
        }

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsedGeneral))
        {
            return DateOnly.FromDateTime(parsedGeneral.Date);
        }

        return null;
    }

    private static string ComputeRowFingerprint(IXLRow row, Dictionary<string, int> colMap)
    {
        // Deterministic fingerprint of the COMPLETE normalized row
        // Sort columns by name for deterministic order across rows
        var sortedCols = colMap.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();

        foreach (var col in sortedCols)
        {
            int colIdx = col.Value;
            var cell = row.Cell(colIdx);
            string normVal = string.Empty;

            if (!cell.IsEmpty())
            {
                if (cell.DataType == XLDataType.DateTime)
                {
                    normVal = DateOnly.FromDateTime(cell.GetDateTime().Date).ToString("yyyy-MM-dd");
                }
                else if (cell.DataType == XLDataType.Number)
                {
                    double num = cell.GetDouble();
                    if (num > 20000 && num < 60000 && (col.Key.Contains("Date", StringComparison.OrdinalIgnoreCase) || col.Key.Contains("LWD", StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            normVal = DateOnly.FromDateTime(DateTime.FromOADate(num).Date).ToString("yyyy-MM-dd");
                        }
                        catch
                        {
                            normVal = num.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    else
                    {
                        normVal = num.ToString(CultureInfo.InvariantCulture);
                    }
                }
                else
                {
                    string rawText = cell.GetString().Trim();
                    if (DateTime.TryParse(rawText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    {
                        normVal = DateOnly.FromDateTime(dt.Date).ToString("yyyy-MM-dd");
                    }
                    else
                    {
                        normVal = Regex.Replace(rawText, @"\s+", " ").ToLowerInvariant();
                    }
                }
            }

            sb.Append(col.Key.ToLowerInvariant()).Append('=').Append(normVal).Append(';');
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static string? GetVal(Dictionary<string, string?> dict, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (dict.TryGetValue(c, out var val) && !string.IsNullOrWhiteSpace(val))
            {
                return val.Trim();
            }
        }
        foreach (var kvp in dict)
        {
            string cleanKey = Regex.Replace(kvp.Key, @"[^a-zA-Z0-9]", "").ToLowerInvariant();
            foreach (var c in candidates)
            {
                string cleanC = Regex.Replace(c, @"[^a-zA-Z0-9]", "").ToLowerInvariant();
                if (cleanKey == cleanC && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    return kvp.Value.Trim();
                }
            }
        }
        return null;
    }

    private static DateOnly? GetDate(IXLRow row, Dictionary<string, int> colMap, Dictionary<string, string?> dict, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (colMap.TryGetValue(c, out int colIdx))
            {
                var d = ParseDateCell(row, colIdx);
                if (d.HasValue) return d;
            }
        }
        var s = GetVal(dict, candidates);
        if (!string.IsNullOrWhiteSpace(s))
        {
            string[] formats = ["yyyy-MM-dd", "dd-MMM-yyyy", "dd-MM-yyyy", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy/MM/dd", "d-MMM-yy", "d-MMM-yyyy"];
            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return DateOnly.FromDateTime(dt.Date);
            }
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
            {
                return DateOnly.FromDateTime(dt2.Date);
            }
        }
        return null;
    }

    private static DateTimeOffset? GetDateTime(IXLRow row, Dictionary<string, int> colMap, Dictionary<string, string?> dict, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (colMap.TryGetValue(c, out int colIdx))
            {
                var cell = row.Cell(colIdx);
                if (!cell.IsEmpty() && cell.DataType == XLDataType.DateTime)
                {
                    return new DateTimeOffset(cell.GetDateTime(), TimeSpan.Zero);
                }
            }
        }
        var s = GetVal(dict, candidates);
        if (!string.IsNullOrWhiteSpace(s))
        {
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            {
                return dto;
            }
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return new DateTimeOffset(dt, TimeSpan.Zero);
            }
        }
        return null;
    }
}

