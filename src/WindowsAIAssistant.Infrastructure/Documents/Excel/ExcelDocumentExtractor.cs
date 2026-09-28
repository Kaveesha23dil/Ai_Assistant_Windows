using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Exceptions;
using WindowsAIAssistant.Core.Models.Documents;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Documents.Excel;

/// <summary>
/// Reads a workbook, one section per worksheet, within fixed limits.
/// <para>
/// The limits are the substance of this extractor rather than a detail of it. A spreadsheet
/// can be arbitrarily larger than any request, and the failure mode without limits is not an
/// error but a hang: the whole sheet is read into memory, then handed to a service that
/// refuses it. So rows, columns, and total cells are each capped, the cap is shared across the
/// whole workbook rather than per sheet, and being cut short is reported rather than hidden.
/// </para>
/// <para>
/// Only stored values are read. Formulas are never evaluated, macros are never run, and no
/// external workbook is opened: a spreadsheet is a program someone else wrote, and this reads
/// the results it left behind without doing any of it.
/// </para>
/// </summary>
public sealed class ExcelDocumentExtractor : IDocumentExtractor
{
    private const string DateFormatMarker = "d";

    private readonly IOptionsMonitor<DocumentOptions> _options;
    private readonly ILogger<ExcelDocumentExtractor> _logger;

    public ExcelDocumentExtractor(
        IOptionsMonitor<DocumentOptions> options,
        ILogger<ExcelDocumentExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public DocumentFileType FileType => DocumentFileType.Excel;

    /// <inheritdoc />
    public bool CanExtract(string extension) =>
        extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<DocumentContent> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var document = SpreadsheetDocument.Open(filePath, isEditable: false);
        var workbookPart = document.WorkbookPart
            ?? throw new DocumentException(
                "This workbook could not be read because it has no workbook content.",
                ErrorCodes.DocumentExtractionFailed);

        var options = _options.CurrentValue;
        var sharedStrings = ReadSharedStrings(workbookPart);
        var sections = new List<DocumentSection>();
        var warnings = new List<DocumentWarning>();
        var cellsRead = 0;
        var truncated = false;

        var sheets = workbookPart.Workbook?.Sheets?.Elements<Sheet>().ToList() ?? [];

        foreach (var sheet in sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relationshipId = sheet.Id?.Value;
            if (relationshipId is null)
            {
                continue;
            }

            var worksheetPart = workbookPart.GetPartById(relationshipId) as WorksheetPart;
            if (worksheetPart is null)
            {
                continue;
            }

            var name = sheet.Name?.Value ?? $"Sheet {sections.Count + 1}";

            // The remaining cell budget is shared: a workbook with two hundred small sheets
            // would otherwise pass a per-sheet limit two hundred times over.
            if (cellsRead >= options.MaximumCells)
            {
                truncated = true;
                break;
            }

            var rendered = RenderWorksheet(
                worksheetPart,
                name,
                sharedStrings,
                options,
                budget: options.MaximumCells - cellsRead,
                cancellationToken,
                out var sheetCells,
                out var sheetTruncated);

            cellsRead += sheetCells;
            truncated |= sheetTruncated;

            if (!string.IsNullOrWhiteSpace(rendered))
            {
                sections.Add(DocumentSection.Create(
                    sections.Count,
                    DocumentSectionKind.Sheet,
                    name,
                    $"Sheet \"{name}\"",
                    rendered));
            }
        }

        if (truncated)
        {
            warnings.Add(DocumentWarningMessages.Create(DocumentWarningKind.SpreadsheetTruncated));
        }

        _logger.LogInformation(
            "Workbook read. Worksheets extracted: {SheetCount}. Cells read: {CellCount}.",
            sections.Count,
            cellsRead);

        var fileName = Path.GetFileName(filePath);
        var properties = document.PackageProperties;

        return Task.FromResult(DocumentContent.Create(
            fileName,
            DocumentFileType.Excel,
            new DocumentMetadata
            {
                FileName = fileName,
                Extension = Path.GetExtension(filePath),
                Title = Blank(properties.Title),
                Author = Blank(properties.Creator),
                SheetCount = sheets.Count,
            },
            sections,
            warnings));
    }

    /// <summary>
    /// Renders one worksheet as rows of tab-separated values, skipping empty rows entirely.
    /// Empty rows are skipped rather than emitted as blanks because a worksheet formatted for
    /// people usually has thousands of them, and they are the main reason a spreadsheet's
    /// extracted text is longer than its data.
    /// </summary>
    private static string RenderWorksheet(
        WorksheetPart worksheetPart,
        string sheetName,
        IReadOnlyList<string> sharedStrings,
        DocumentOptions options,
        int budget,
        CancellationToken cancellationToken,
        out int cellsRead,
        out bool truncated)
    {
        var sheetData = worksheetPart.Worksheet?.GetFirstChild<SheetData>();
        if (sheetData is null)
        {
            cellsRead = 0;
            truncated = false;
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Sheet \"{sheetName}\"");
        cellsRead = 0;
        truncated = false;
        var rows = 0;

        foreach (var row in sheetData.Elements<Row>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rows >= options.MaximumRowsPerSheet)
            {
                truncated = true;
                break;
            }

            var values = new List<string>();
            var columns = 0;

            foreach (var cell in row.Elements<Cell>())
            {
                if (columns >= options.MaximumColumnsPerSheet || cellsRead >= budget)
                {
                    truncated = true;
                    break;
                }

                var value = ReadCellValue(cell, sharedStrings);
                columns++;
                cellsRead++;

                if (value.Length > 0)
                {
                    values.Add(value);
                }
            }

            if (values.Count > 0)
            {
                builder.AppendLine(string.Join("\t", values));
                rows++;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Reads a cell's stored value as the number or text it displays.
    /// <para>
    /// A formula cell is read from the value Excel cached against it, not by working the
    /// formula out. The cached value is what the last calculation produced and is what the
    /// person saw when they looked at the file, which is the only version worth summarizing;
    /// evaluating it here would mean reimplementing a spreadsheet engine and would risk
    /// producing a different answer from the one on screen.
    /// </para>
    /// </summary>
    private static string ReadCellValue(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        var raw = cell.CellValue?.InnerText;
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(raw, CultureInfo.InvariantCulture, out var index) &&
            index >= 0 && index < sharedStrings.Count)
        {
            return sharedStrings[index].Replace('\t', ' ').Replace('\n', ' ');
        }

        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return ReadInlineString(cell);
        }

        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return raw == "1" ? "TRUE" : "FALSE";
        }

        // A date is stored as a number and only looks like a date because of the format
        // applied to the cell. Without the format it would be reported as 45000, which is
        // worse than useless in a summary; the serial is converted, and an unparseable one
        // falls back to the raw number rather than being guessed at.
        if (cell.StyleIndex is not null && LooksLikeDate(workbookStyles: cell))
        {
            if (double.TryParse(raw, CultureInfo.InvariantCulture, out var serial))
            {
                return FormatSerial(serial);
            }
        }

        return raw.Replace('\t', ' ').Replace('\n', ' ');
    }

    private static string ReadInlineString(Cell cell)
    {
        var inline = cell.InlineString;
        if (inline?.Text?.Text is { Length: > 0 } text)
        {
            return text.Replace('\t', ' ').Replace('\n', ' ');
        }

        var runs = inline?.Descendants<DocumentFormat.OpenXml.Spreadsheet.Text>().ToList();
        return runs is null || runs.Count == 0
            ? string.Empty
            : string.Concat(runs.Select(run => run.Text)).Replace('\t', ' ').Replace('\n', ' ');
    }

    /// <summary>
    /// Whether a cell carries one of the built-in date number formats. The style index is
    /// checked against the small set of built-in format ids Excel reserves for dates, which
    /// needs no stylesheet lookup and covers the overwhelmingly common case of a date someone
    /// typed into a cell.
    /// </summary>
    private static bool LooksLikeDate(Cell workbookStyles) =>
        workbookStyles.StyleIndex?.Value is >= 14 and <= 22 or >= 45 and <= 47;

    private static string FormatSerial(double serial)
    {
        // Excel counts days from 1899-12-30, which accounts for the leap year it treats as
        // real in its own arithmetic.
        var date = new DateTime(1899, 12, 30).AddDays(serial);

        return date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> ReadSharedStrings(WorkbookPart workbookPart)
    {
        var part = workbookPart.SharedStringTablePart;
        if (part?.SharedStringTable is null)
        {
            return [];
        }

        var values = new List<string>();

        foreach (var item in part.SharedStringTable.Elements<SharedStringItem>())
        {
            // A shared string can be split across several runs for formatting reasons, and the
            // runs are one string to a reader.
            values.Add(string.Concat(item.Descendants<DocumentFormat.OpenXml.Spreadsheet.Text>()
                .Select(text => text.Text)));
        }

        return values;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
