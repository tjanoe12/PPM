namespace PPM.Shared.DTOs;

/// <summary>
/// Represents a complete rack assignment with all its fractions.
/// </summary>
public record RackAssignmentDto
{
    /// <summary>
    /// Barcode identifying this rack.
    /// </summary>
    public string RackBarcode { get; init; } = string.Empty;

    /// <summary>
    /// Collection of fractions assigned to this rack.
    /// </summary>
    public IReadOnlyList<FractionDto> Fractions { get; init; } = [];

    /// <summary>
    /// Total number of fractions in this rack.
    /// </summary>
    public int TotalFractions => Fractions.Count;

    /// <summary>
    /// Indicates whether any fractions have vial barcodes.
    /// </summary>
    public bool HasVialBarcodes => Fractions.Any(f => !string.IsNullOrEmpty(f.VialBarcode));

    /// <summary>
    /// Indicates whether any fractions have gross weight data.
    /// </summary>
    public bool HasGrossWeights => Fractions.Any(f => f.GrossWeight.HasValue);

    /// <summary>
    /// Indicates whether any fractions have net weight data.
    /// </summary>
    public bool HasNetWeights => Fractions.Any(f => f.NetWeight.HasValue);
}
