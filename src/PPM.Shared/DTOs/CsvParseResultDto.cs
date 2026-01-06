namespace PPM.Shared.DTOs;

/// <summary>
/// Result of parsing a CSV file containing fraction data.
/// </summary>
public record CsvParseResultDto
{
    /// <summary>
    /// Indicates whether parsing was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if parsing failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The parsed rack assignment with all fractions.
    /// Null if parsing failed.
    /// </summary>
    public RackAssignmentDto? RackAssignment { get; init; }

    /// <summary>
    /// Information about which columns were detected and mapped.
    /// </summary>
    public CsvColumnMapping? DetectedMapping { get; init; }

    /// <summary>
    /// Indicates whether the CSV file had a header row.
    /// </summary>
    public bool HasHeaders { get; init; }

    /// <summary>
    /// Number of data rows successfully parsed.
    /// </summary>
    public int RowsParsed { get; init; }

    /// <summary>
    /// Original filename of the uploaded CSV.
    /// </summary>
    public string? FileName { get; init; }
}

/// <summary>
/// Describes the detected column mapping from CSV parsing.
/// </summary>
public record CsvColumnMapping
{
    /// <summary>
    /// Zero-based index of the vial barcode column, if detected.
    /// </summary>
    public int? VialBarcodeColumn { get; init; }

    /// <summary>
    /// Zero-based index of the rack barcode column, if detected.
    /// </summary>
    public int? RackBarcodeColumn { get; init; }

    /// <summary>
    /// Zero-based index of the tare weight column, if detected.
    /// </summary>
    public int? TareWeightColumn { get; init; }

    /// <summary>
    /// Zero-based index of the gross weight column, if detected.
    /// </summary>
    public int? GrossWeightColumn { get; init; }

    /// <summary>
    /// Zero-based index of the net weight column, if detected.
    /// </summary>
    public int? NetWeightColumn { get; init; }

    /// <summary>
    /// Header values from the first row, if headers were detected.
    /// </summary>
    public string[] Headers { get; init; } = [];

    /// <summary>
    /// Total number of columns in the CSV.
    /// </summary>
    public int TotalColumns { get; init; }
}
