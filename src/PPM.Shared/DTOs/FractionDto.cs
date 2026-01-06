namespace PPM.Shared.DTOs;

/// <summary>
/// Represents a single fraction/vial parsed from CSV data.
/// </summary>
public record FractionDto
{
    /// <summary>
    /// Unique barcode identifying this vial/fraction.
    /// May be null if CSV doesn't contain vial barcodes.
    /// </summary>
    public string? VialBarcode { get; init; }

    /// <summary>
    /// Barcode of the rack containing this fraction.
    /// Typically the same across all fractions in a single CSV file.
    /// </summary>
    public string? RackBarcode { get; init; }

    /// <summary>
    /// Tare weight (empty vial weight) in grams.
    /// </summary>
    public decimal TareWeight { get; init; }

    /// <summary>
    /// Gross weight (vial + contents) in grams.
    /// Null if not present in CSV.
    /// </summary>
    public decimal? GrossWeight { get; init; }

    /// <summary>
    /// Net weight (contents only) in grams.
    /// Null if not present in CSV.
    /// </summary>
    public decimal? NetWeight { get; init; }

    /// <summary>
    /// Row number from the source CSV (1-indexed).
    /// </summary>
    public int RowNumber { get; init; }
}
