using PPM.Shared.DTOs;

namespace PPM.Server.Services;

/// <summary>
/// Service for parsing CSV files containing HPLC fraction data.
/// </summary>
public interface ICsvParserService
{
    /// <summary>
    /// Parses a CSV stream and extracts fraction data with intelligent column detection.
    /// </summary>
    /// <param name="csvStream">The CSV file stream.</param>
    /// <param name="fileName">Original filename for context.</param>
    /// <returns>Parse result containing fractions and detected mapping.</returns>
    Task<CsvParseResultDto> ParseAsync(Stream csvStream, string fileName);
}
