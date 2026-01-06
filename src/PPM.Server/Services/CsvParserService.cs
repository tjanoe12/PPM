using System.Globalization;
using System.Text;
using PPM.Shared.DTOs;

namespace PPM.Server.Services;

/// <summary>
/// Parses CSV files containing HPLC fraction data with intelligent column detection.
/// Automatically detects headers, barcodes, and weight columns without user configuration.
/// </summary>
public class CsvParserService : ICsvParserService
{
    // Keywords for header detection
    private static readonly string[] BarcodeKeywords =
        ["barcode", "vial", "fraction", "sample", "id", "tube", "well"];
    private static readonly string[] RackKeywords =
        ["rack", "plate", "tray", "container", "box"];
    private static readonly string[] TareKeywords =
        ["tare", "empty"];
    private static readonly string[] GrossKeywords =
        ["gross", "full", "total", "filled"];
    private static readonly string[] NetKeywords =
        ["net", "content", "sample"];
    private static readonly string[] WeightKeywords =
        ["weight", "wt", "mass", "g", "mg", "gram"];

    public async Task<CsvParseResultDto> ParseAsync(Stream csvStream, string fileName)
    {
        try
        {
            using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = await reader.ReadToEndAsync();

            return Parse(content, fileName);
        }
        catch (Exception ex)
        {
            return new CsvParseResultDto
            {
                Success = false,
                ErrorMessage = $"Failed to read CSV file: {ex.Message}",
                FileName = fileName
            };
        }
    }

    private CsvParseResultDto Parse(string content, string fileName)
    {
        // Split into lines and filter empty
        var lines = content
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
        {
            return new CsvParseResultDto
            {
                Success = false,
                ErrorMessage = "CSV file is empty",
                FileName = fileName
            };
        }

        // Detect delimiter
        var delimiter = DetectDelimiter(lines[0]);

        // Parse all rows into columns
        var allRows = lines.Select(l => ParseRow(l, delimiter)).ToList();

        // Detect if first row is a header
        var hasHeaders = DetectHeaders(allRows[0]);
        var headers = hasHeaders ? allRows[0] : [];
        var dataRows = hasHeaders ? allRows.Skip(1).ToList() : allRows;

        if (dataRows.Count == 0)
        {
            return new CsvParseResultDto
            {
                Success = false,
                ErrorMessage = "No data rows found (file may only contain headers)",
                FileName = fileName,
                HasHeaders = hasHeaders
            };
        }

        // Analyze columns to determine their types
        var mapping = AnalyzeColumns(headers, dataRows);

        // Extract fractions from data
        var fractions = ExtractFractions(dataRows, mapping);

        // Determine the rack barcode
        var rackBarcode = DetermineRackBarcode(fractions, mapping)
            ?? ExtractRackBarcodeFromFilename(fileName)
            ?? "UNKNOWN";

        // Update fractions with rack barcode if not set
        if (string.IsNullOrEmpty(fractions.FirstOrDefault()?.RackBarcode))
        {
            fractions = fractions
                .Select(f => f with { RackBarcode = rackBarcode })
                .ToList();
        }

        return new CsvParseResultDto
        {
            Success = true,
            HasHeaders = hasHeaders,
            RowsParsed = dataRows.Count,
            FileName = fileName,
            DetectedMapping = mapping,
            RackAssignment = new RackAssignmentDto
            {
                RackBarcode = rackBarcode,
                Fractions = fractions
            }
        };
    }

    /// <summary>
    /// Detects the most likely delimiter character in the CSV.
    /// </summary>
    private static char DetectDelimiter(string line)
    {
        var delimiters = new[] { ',', '\t', ';', '|' };
        var counts = delimiters.Select(d => new { Delimiter = d, Count = CountDelimiter(line, d) });
        return counts.OrderByDescending(x => x.Count).First().Delimiter;
    }

    private static int CountDelimiter(string line, char delimiter)
    {
        int count = 0;
        bool inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == delimiter && !inQuotes) count++;
        }
        return count;
    }

    /// <summary>
    /// Parses a single CSV row, handling quoted fields.
    /// </summary>
    private static string[] ParseRow(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                // Handle escaped quotes ("") inside quoted strings
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // Skip next quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString().Trim());

        return result.ToArray();
    }

    /// <summary>
    /// Determines if the first row is a header row based on keyword matching.
    /// </summary>
    private static bool DetectHeaders(string[] firstRow)
    {
        if (firstRow.Length == 0) return false;

        var allKeywords = BarcodeKeywords
            .Concat(RackKeywords)
            .Concat(TareKeywords)
            .Concat(GrossKeywords)
            .Concat(NetKeywords)
            .Concat(WeightKeywords);

        // Count how many cells contain keywords
        int keywordMatches = firstRow.Count(cell =>
            allKeywords.Any(kw => cell.Contains(kw, StringComparison.OrdinalIgnoreCase)));

        // If at least one keyword match, likely headers
        if (keywordMatches > 0) return true;

        // Also check if first row has no numeric values while other rows would
        bool firstRowAllText = firstRow.All(cell => !IsNumeric(cell));
        return firstRowAllText;
    }

    /// <summary>
    /// Analyzes all columns to determine their semantic meaning.
    /// </summary>
    private static CsvColumnMapping AnalyzeColumns(string[] headers, List<string[]> dataRows)
    {
        int columnCount = dataRows.Max(r => r.Length);
        var columnAnalysis = new ColumnAnalysis[columnCount];

        // Initialize analysis for each column
        for (int col = 0; col < columnCount; col++)
        {
            columnAnalysis[col] = AnalyzeColumn(col, headers, dataRows);
        }

        // Determine column assignments based on analysis
        int? tareCol = null, grossCol = null, netCol = null;
        int? vialBarcodeCol = null, rackBarcodeCol = null;

        // First pass: Use headers if available
        if (headers.Length > 0)
        {
            for (int i = 0; i < headers.Length && i < columnCount; i++)
            {
                var header = headers[i].ToLowerInvariant();

                if (ContainsAny(header, TareKeywords) && ContainsAny(header, WeightKeywords.Concat([""])))
                    tareCol = i;
                else if (ContainsAny(header, GrossKeywords))
                    grossCol = i;
                else if (ContainsAny(header, NetKeywords) && !ContainsAny(header, RackKeywords))
                    netCol = i;
                else if (ContainsAny(header, RackKeywords))
                    rackBarcodeCol = i;
                else if (ContainsAny(header, BarcodeKeywords) && rackBarcodeCol != i)
                    vialBarcodeCol ??= i;
            }
        }

        // Second pass: Infer from data patterns
        var numericColumns = columnAnalysis
            .Select((a, i) => new { Index = i, Analysis = a })
            .Where(x => x.Analysis.IsNumeric)
            .OrderBy(x => x.Analysis.AverageValue)
            .ToList();

        var stringColumns = columnAnalysis
            .Select((a, i) => new { Index = i, Analysis = a })
            .Where(x => !x.Analysis.IsNumeric && x.Analysis.HasValues)
            .ToList();

        // Assign weight columns if not already set from headers
        if (tareCol == null && numericColumns.Count > 0)
        {
            if (numericColumns.Count == 1)
            {
                // Single numeric column = tare weight
                tareCol = numericColumns[0].Index;
            }
            else if (numericColumns.Count == 2)
            {
                // Two numeric: lower average = tare, higher = gross
                tareCol = numericColumns[0].Index;
                grossCol = numericColumns[1].Index;
            }
            else if (numericColumns.Count >= 3)
            {
                // Three or more: highest = gross, middle = tare, lowest could be net or sequence
                // Pattern: Gross, Tare, Net OR sequence columns
                grossCol = numericColumns[^1].Index;    // Highest
                tareCol = numericColumns[^2].Index;     // Second highest (middle)
                netCol = numericColumns[^3].Index;      // Third highest

                // Verify net makes sense (should be gross - tare approximately)
                var grossAvg = numericColumns[^1].Analysis.AverageValue;
                var tareAvg = numericColumns[^2].Analysis.AverageValue;
                var netAvg = numericColumns[^3].Analysis.AverageValue;
                var expectedNet = grossAvg - tareAvg;

                // If the "net" column doesn't match expected, it might be a sequence/ID column
                if (Math.Abs(netAvg - expectedNet) > expectedNet * 0.5m)
                {
                    netCol = null;
                }
            }
        }

        // Assign barcode columns from string analysis
        if (vialBarcodeCol == null || rackBarcodeCol == null)
        {
            foreach (var col in stringColumns)
            {
                // Skip columns already assigned
                if (col.Index == tareCol || col.Index == grossCol || col.Index == netCol)
                    continue;

                // Rack barcode: all values are identical (or nearly so)
                if (rackBarcodeCol == null && col.Analysis.UniquenessRatio < 0.1m)
                {
                    rackBarcodeCol = col.Index;
                }
                // Vial barcode: each value is unique
                else if (vialBarcodeCol == null && col.Analysis.UniquenessRatio > 0.9m)
                {
                    vialBarcodeCol = col.Index;
                }
            }
        }

        return new CsvColumnMapping
        {
            Headers = headers,
            TotalColumns = columnCount,
            TareWeightColumn = tareCol,
            GrossWeightColumn = grossCol,
            NetWeightColumn = netCol,
            VialBarcodeColumn = vialBarcodeCol,
            RackBarcodeColumn = rackBarcodeCol
        };
    }

    /// <summary>
    /// Analyzes a single column's data characteristics.
    /// </summary>
    private static ColumnAnalysis AnalyzeColumn(int columnIndex, string[] headers, List<string[]> dataRows)
    {
        var values = dataRows
            .Where(r => r.Length > columnIndex)
            .Select(r => r[columnIndex])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        if (values.Count == 0)
        {
            return new ColumnAnalysis { HasValues = false };
        }

        var numericValues = values
            .Select(v => (Success: TryParseDecimal(v, out var d), Value: d))
            .Where(x => x.Success)
            .Select(x => x.Value)
            .ToList();

        int uniqueCount = values.Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return new ColumnAnalysis
        {
            HasValues = true,
            TotalValues = values.Count,
            IsNumeric = numericValues.Count >= values.Count * 0.8, // 80% threshold
            AverageValue = numericValues.Count > 0 ? numericValues.Average() : 0,
            MinValue = numericValues.Count > 0 ? numericValues.Min() : 0,
            MaxValue = numericValues.Count > 0 ? numericValues.Max() : 0,
            UniqueCount = uniqueCount,
            UniquenessRatio = values.Count > 0 ? (decimal)uniqueCount / values.Count : 0
        };
    }

    /// <summary>
    /// Extracts fraction DTOs from parsed data rows using the detected mapping.
    /// </summary>
    private static List<FractionDto> ExtractFractions(List<string[]> dataRows, CsvColumnMapping mapping)
    {
        var fractions = new List<FractionDto>();

        for (int i = 0; i < dataRows.Count; i++)
        {
            var row = dataRows[i];

            var fraction = new FractionDto
            {
                RowNumber = i + 1,
                VialBarcode = GetStringValue(row, mapping.VialBarcodeColumn),
                RackBarcode = GetStringValue(row, mapping.RackBarcodeColumn),
                TareWeight = GetDecimalValue(row, mapping.TareWeightColumn) ?? 0m,
                GrossWeight = GetDecimalValue(row, mapping.GrossWeightColumn),
                NetWeight = GetDecimalValue(row, mapping.NetWeightColumn)
            };

            fractions.Add(fraction);
        }

        return fractions;
    }

    /// <summary>
    /// Determines the rack barcode from parsed fractions.
    /// </summary>
    private static string? DetermineRackBarcode(List<FractionDto> fractions, CsvColumnMapping mapping)
    {
        if (!mapping.RackBarcodeColumn.HasValue) return null;

        return fractions
            .Select(f => f.RackBarcode)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .GroupBy(b => b)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;
    }

    /// <summary>
    /// Attempts to extract a rack barcode from the filename.
    /// </summary>
    private static string? ExtractRackBarcodeFromFilename(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        // Remove extension
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(name)) return null;

        // Common patterns: RACK-001, R001, Rack_123, etc.
        // Just return the filename without extension as the rack identifier
        return name;
    }

    private static string? GetStringValue(string[] row, int? column)
    {
        if (!column.HasValue || row.Length <= column.Value) return null;
        var value = row[column.Value].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static decimal? GetDecimalValue(string[] row, int? column)
    {
        var stringValue = GetStringValue(row, column);
        if (stringValue == null) return null;
        return TryParseDecimal(stringValue, out var value) ? value : null;
    }

    private static bool IsNumeric(string value)
    {
        return TryParseDecimal(value, out _);
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        // Handle various number formats
        value = value.Trim();

        // Try standard parsing
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            return true;

        // Try with comma as decimal separator (European format)
        if (decimal.TryParse(value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            return true;

        return false;
    }

    private static bool ContainsAny(string text, IEnumerable<string> keywords)
    {
        return keywords.Any(kw =>
            !string.IsNullOrEmpty(kw) &&
            text.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private record ColumnAnalysis
    {
        public bool HasValues { get; init; }
        public int TotalValues { get; init; }
        public bool IsNumeric { get; init; }
        public decimal AverageValue { get; init; }
        public decimal MinValue { get; init; }
        public decimal MaxValue { get; init; }
        public int UniqueCount { get; init; }
        public decimal UniquenessRatio { get; init; }
    }
}
