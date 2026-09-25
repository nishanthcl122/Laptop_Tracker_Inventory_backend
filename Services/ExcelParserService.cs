using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using LaptopTracking.Api.Models.DTOs;
using LaptopTracking.Api.Models.Entities;

namespace LaptopTracking.Api.Services;

public class ExcelParseResult
{
    public List<ImportBatchRow> ValidRows { get; set; } = [];
    public List<ValidationErrorDto> Errors { get; set; } = [];
}

public class ExcelParserService
{
    public ExcelParseResult ParseAndValidate(Stream stream, int batchId)
    {
        var result = new ExcelParseResult();
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.Count > 0 ? workbook.Worksheet(1) : null;

        if (worksheet == null)
        {
            result.Errors.Add(new ValidationErrorDto
            {
                Row = 1,
                Field = "File",
                Message = "The uploaded Excel file contains no worksheets."
            });
            return result;
        }

        var headerRow = worksheet.FirstRowUsed();
        if (headerRow == null)
        {
            result.Errors.Add(new ValidationErrorDto
            {
                Row = 1,
                Field = "File",
                Message = "The worksheet is completely empty."
            });
            return result;
        }

        // Map header column names to indexes
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var headerText = cell.GetString().Trim();
            if (!string.IsNullOrEmpty(headerText))
            {
                colMap[headerText] = cell.Address.ColumnNumber;
            }
        }

        int serialCol = FindColumn(colMap, "Serial No.", "Serial No", "Laptop Serial Number", "Serial Number", "SerialNumber", "Serial");
        int fbrCol = FindColumn(colMap, "FBR Request", "FBRRequest", "Laptop FBR ID", "Laptop FBR Number", "FBR ID", "FBR Number", "FBR");
        int locationCol = FindColumn(colMap, "Location", "City", "Office Location");
        int statusCol = FindColumn(colMap, "Status", "Allocation Status", "AllocationStatus");
        int itSpocCol = FindColumn(colMap, "IT SPOC", "ITSPOC", "IT_SPOC", "SPOC");
        int sapCol = FindColumn(colMap, "SAP ID", "Employee SAP ID", "SAPID", "SAP");
        int userCol = FindColumn(colMap, "User Name", "UserName", "Employee Name", "User", "Employee", "Name", "Emp Name");
        int rasCol = FindColumn(colMap, "RAS Status", "RAS", "RASStatus");
        int startCol = FindColumn(colMap, "Start Date", "StartDate", "Allocation Start Date", "Assignment Date");
        int endCol = FindColumn(colMap, "End Date", "EndDate", "Allocation End Date");
        int lwdCol = FindColumn(colMap, "Last Working Day", "LWD", "LastWorkingDay");

        if (serialCol == -1)
        {
            result.Errors.Add(new ValidationErrorDto
            {
                Row = headerRow.RowNumber(),
                Field = "Header",
                Message = "Required column 'Serial No.' / 'Laptop Serial Number' was not found in the Excel header."
            });
            return result;
        }

        var seenSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();

        for (int rowNum = headerRow.RowNumber() + 1; rowNum <= lastRowNumber; rowNum++)
        {
            var row = worksheet.Row(rowNum);
            if (row.IsEmpty()) continue;

            string rawSerial = GetCellString(row, serialCol);
            if (string.IsNullOrWhiteSpace(rawSerial))
            {
                result.Errors.Add(new ValidationErrorDto
                {
                    Row = rowNum,
                    Field = "Serial Number",
                    Message = "Laptop Serial Number is missing or blank."
                });
                continue;
            }

            string serial = rawSerial.Trim().ToUpperInvariant();

            if (seenSerials.Contains(serial))
            {
                result.Errors.Add(new ValidationErrorDto
                {
                    Row = rowNum,
                    Field = "Serial Number",
                    Message = $"Duplicate serial number '{serial}' found within the same file."
                });
                continue;
            }
            seenSerials.Add(serial);

            string rawFbr = GetCellString(row, fbrCol);
            string fbr = string.IsNullOrWhiteSpace(rawFbr) ? string.Empty : rawFbr.Trim();

            string rawLoc = GetCellString(row, locationCol);
            string location = string.IsNullOrWhiteSpace(rawLoc) ? string.Empty : rawLoc.Trim();

            string rawStatus = GetCellString(row, statusCol);
            string status = NormalizeStatus(rawStatus);
            if (status == "INVALID")
            {
                result.Errors.Add(new ValidationErrorDto
                {
                    Row = rowNum,
                    Field = "Status",
                    Message = $"Invalid Status '{rawStatus}'. Must be 'Allocated' or 'In Stock'."
                });
                continue;
            }

            string? itSpoc = itSpocCol > 0 ? NormalizeSpaces(GetCellString(row, itSpocCol)) : null;

            string rawSap = GetCellString(row, sapCol);
            string? sapId = string.IsNullOrWhiteSpace(rawSap) ? null : rawSap.Trim().ToUpperInvariant();

            string rawUser = GetCellString(row, userCol);
            string? userName = NormalizeSpaces(rawUser);

            string rawRas = GetCellString(row, rasCol);
            string rasStatus = NormalizeRasStatus(rawRas);

            DateOnly? startDate = ParseDateCell(row, startCol);
            DateOnly? endDate = ParseDateCell(row, endCol);
            DateOnly? lwd = ParseDateCell(row, lwdCol);

            var batchRow = new ImportBatchRow
            {
                BatchId = batchId,
                SerialNumber = serial,
                FBRRequest = fbr,
                Location = location,
                Status = status,
                ITSPOC = itSpoc,
                SAPId = sapId,
                UserName = userName,
                RASStatus = rasStatus,
                StartDate = startDate,
                EndDate = endDate,
                LastWorkingDay = lwd,
                ValidationStatus = "VALID"
            };

            result.ValidRows.Add(batchRow);
        }

        return result;
    }

    private static int FindColumn(Dictionary<string, int> map, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (map.TryGetValue(c, out int col)) return col;
        }

        // Fuzzy match ignoring non-alphanumeric
        foreach (var kvp in map)
        {
            string cleanKvp = Regex.Replace(kvp.Key, @"[^a-zA-Z0-9]", "");
            foreach (var c in candidates)
            {
                string cleanC = Regex.Replace(c, @"[^a-zA-Z0-9]", "");
                if (cleanKvp.Equals(cleanC, StringComparison.OrdinalIgnoreCase))
                {
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
        return cell.GetString();
    }

    private static string NormalizeStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "INVALID";
        string s = raw.Trim().ToUpperInvariant().Replace("_", " ");
        if (s == "ALLOCATED" || s == "ASSIGNED") return "Allocated";
        if (s == "IN STOCK" || s == "INSTOCK" || s == "STOCK" || s == "AVAILABLE") return "In Stock";
        return "INVALID";
    }

    private static string NormalizeRasStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "UNKNOWN";
        string s = raw.Trim().ToUpperInvariant();
        if (s == "ACTIVE" || s == "ACT") return "ACTIVE";
        if (s == "INACTIVE" || s == "INACT" || s == "IN-ACTIVE" || s == "LOST") return "INACTIVE";
        return "UNKNOWN";
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
            catch
            {
                // Continue to string parsing
            }
        }

        string text = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;

        string[] formats = ["yyyy-MM-dd", "dd-MMM-yyyy", "dd-MM-yyyy", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy/MM/dd", "d-MMM-yy", "d-MMM-yyyy"];
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
}
