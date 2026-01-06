# PPM - HPLC Fraction CSV Parser & Rack Assignment UI

## Project Overview

A .NET 8 Blazor Server/Client application using MudBlazor for assigning CSV files containing fraction data to analytical chemistry HPLC racks. The system intelligently parses CSV files without requiring column mapping by inferring data types.

## Technology Stack

- **.NET 8** - Target framework
- **Blazor Server/WebAssembly** - Hybrid hosting model
- **MudBlazor** - Material Design component library
- **CsvHelper** - CSV parsing library

## Project Structure

```
PPM/
├── PPM.sln
├── src/
│   ├── PPM.Client/                    # Blazor WebAssembly client
│   │   ├── PPM.Client.csproj
│   │   ├── Pages/
│   │   │   └── RackAssignment.razor   # Main rack assignment page
│   │   ├── Components/
│   │   │   ├── CsvDropZone.razor      # Drag-drop CSV upload
│   │   │   ├── FractionGrid.razor     # Fraction data preview grid
│   │   │   └── RackVisualizer.razor   # Visual rack representation
│   │   └── Services/
│   │       └── ICsvParserService.cs   # Client-side interface
│   │
│   ├── PPM.Server/                    # Blazor Server + API
│   │   ├── PPM.Server.csproj
│   │   ├── Controllers/
│   │   │   └── FractionController.cs  # API endpoints
│   │   └── Services/
│   │       └── CsvParserService.cs    # CSV parsing implementation
│   │
│   └── PPM.Shared/                    # Shared DTOs and models
│       ├── PPM.Shared.csproj
│       ├── DTOs/
│       │   ├── FractionDto.cs
│       │   ├── RackAssignmentDto.cs
│       │   └── CsvParseResultDto.cs
│       └── Models/
│           └── ParsedFraction.cs
```

---

## 1. CSV Parser Implementation

### Parsing Strategy

The CSV parser uses intelligent inference to identify columns without user configuration:

1. **Barcode Detection**:
   - Fraction barcodes are unique per row (alphanumeric, often formatted like `F001`, `VL-12345`)
   - Rack barcodes repeat across all rows (same value in entire column)

2. **Weight Detection** (numeric columns):
   - **1 number**: Tare weight
   - **2 numbers**: Lower = Tare, Higher = Gross
   - **3 numbers**: Middle value = Tare (typical pattern: Gross, Tare, Net)

3. **Header Detection**:
   - First row analyzed for keywords: `tare`, `gross`, `net`, `weight`, `barcode`, `vial`, `fraction`, `rack`
   - If no keywords found, treat first row as data

### DTOs

```csharp
// PPM.Shared/DTOs/FractionDto.cs
namespace PPM.Shared.DTOs;

public record FractionDto
{
    public string? VialBarcode { get; init; }
    public string? RackBarcode { get; init; }
    public decimal TareWeight { get; init; }
    public decimal? GrossWeight { get; init; }
    public decimal? NetWeight { get; init; }
    public int RowNumber { get; init; }
}

// PPM.Shared/DTOs/RackAssignmentDto.cs
namespace PPM.Shared.DTOs;

public record RackAssignmentDto
{
    public string RackBarcode { get; init; } = string.Empty;
    public IReadOnlyList<FractionDto> Fractions { get; init; } = [];
    public int TotalFractions => Fractions.Count;
    public bool HasVialBarcodes => Fractions.Any(f => !string.IsNullOrEmpty(f.VialBarcode));
}

// PPM.Shared/DTOs/CsvParseResultDto.cs
namespace PPM.Shared.DTOs;

public record CsvParseResultDto
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public RackAssignmentDto? RackAssignment { get; init; }
    public CsvColumnMapping? DetectedMapping { get; init; }
    public bool HasHeaders { get; init; }
    public int RowsParsed { get; init; }
}

public record CsvColumnMapping
{
    public int? VialBarcodeColumn { get; init; }
    public int? RackBarcodeColumn { get; init; }
    public int? TareWeightColumn { get; init; }
    public int? GrossWeightColumn { get; init; }
    public int? NetWeightColumn { get; init; }
    public string[] Headers { get; init; } = [];
}
```

### Parser Service

```csharp
// PPM.Server/Services/CsvParserService.cs
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using PPM.Shared.DTOs;

namespace PPM.Server.Services;

public interface ICsvParserService
{
    Task<CsvParseResultDto> ParseAsync(Stream csvStream, string fileName);
}

public class CsvParserService : ICsvParserService
{
    private static readonly string[] BarcodeKeywords =
        ["barcode", "vial", "fraction", "sample", "id", "tube"];
    private static readonly string[] RackKeywords =
        ["rack", "plate", "tray", "container"];
    private static readonly string[] TareKeywords =
        ["tare", "empty"];
    private static readonly string[] GrossKeywords =
        ["gross", "full", "total"];
    private static readonly string[] WeightKeywords =
        ["weight", "wt", "mass", "g", "mg"];

    public async Task<CsvParseResultDto> ParseAsync(Stream csvStream, string fileName)
    {
        try
        {
            using var reader = new StreamReader(csvStream);
            var content = await reader.ReadToEndAsync();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                               .Select(l => l.Trim('\r'))
                               .Where(l => !string.IsNullOrWhiteSpace(l))
                               .ToList();

            if (lines.Count == 0)
                return new CsvParseResultDto { Success = false, ErrorMessage = "Empty CSV file" };

            // Detect delimiter
            var delimiter = DetectDelimiter(lines[0]);

            // Parse all rows
            var allRows = lines.Select(l => ParseRow(l, delimiter)).ToList();

            // Detect if first row is header
            var hasHeaders = DetectHeaders(allRows[0]);
            var headers = hasHeaders ? allRows[0] : [];
            var dataRows = hasHeaders ? allRows.Skip(1).ToList() : allRows;

            if (dataRows.Count == 0)
                return new CsvParseResultDto { Success = false, ErrorMessage = "No data rows found" };

            // Analyze columns
            var mapping = AnalyzeColumns(headers, dataRows);

            // Extract fractions
            var fractions = ExtractFractions(dataRows, mapping);

            // Determine rack barcode (most common non-unique barcode)
            var rackBarcode = DetermineRackBarcode(fractions, mapping);

            return new CsvParseResultDto
            {
                Success = true,
                HasHeaders = hasHeaders,
                RowsParsed = dataRows.Count,
                DetectedMapping = mapping,
                RackAssignment = new RackAssignmentDto
                {
                    RackBarcode = rackBarcode ?? fileName,
                    Fractions = fractions
                }
            };
        }
        catch (Exception ex)
        {
            return new CsvParseResultDto
            {
                Success = false,
                ErrorMessage = $"Parse error: {ex.Message}"
            };
        }
    }

    private static char DetectDelimiter(string line)
    {
        var delimiters = new[] { ',', '\t', ';', '|' };
        return delimiters.OrderByDescending(d => line.Count(c => c == d)).First();
    }

    private static string[] ParseRow(string line, char delimiter)
    {
        // Simple CSV parsing - handles basic quoting
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == delimiter && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else current.Append(c);
        }
        result.Add(current.ToString().Trim());
        return result.ToArray();
    }

    private static bool DetectHeaders(string[] firstRow)
    {
        var allKeywords = BarcodeKeywords
            .Concat(RackKeywords)
            .Concat(TareKeywords)
            .Concat(GrossKeywords)
            .Concat(WeightKeywords);

        // If any cell contains a keyword, likely a header row
        return firstRow.Any(cell =>
            allKeywords.Any(kw =>
                cell.Contains(kw, StringComparison.OrdinalIgnoreCase)));
    }

    private static CsvColumnMapping AnalyzeColumns(string[] headers, List<string[]> dataRows)
    {
        var columnCount = dataRows.Max(r => r.Length);
        var numericColumns = new List<int>();
        var stringColumns = new List<int>();
        var uniqueValueCounts = new Dictionary<int, int>();

        // Analyze each column
        for (int col = 0; col < columnCount; col++)
        {
            var values = dataRows
                .Where(r => r.Length > col)
                .Select(r => r[col])
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            if (values.Count == 0) continue;

            var uniqueCount = values.Distinct().Count();
            uniqueValueCounts[col] = uniqueCount;

            // Check if column is numeric
            var numericCount = values.Count(v => decimal.TryParse(v, out _));
            if (numericCount > values.Count * 0.8) // 80% threshold
                numericColumns.Add(col);
            else
                stringColumns.Add(col);
        }

        int? tareCol = null, grossCol = null, netCol = null;
        int? vialBarcodeCol = null, rackBarcodeCol = null;

        // Use headers if available
        if (headers.Length > 0)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                var h = headers[i].ToLowerInvariant();

                if (TareKeywords.Any(k => h.Contains(k)))
                    tareCol = i;
                else if (GrossKeywords.Any(k => h.Contains(k)))
                    grossCol = i;
                else if (h.Contains("net"))
                    netCol = i;
                else if (RackKeywords.Any(k => h.Contains(k)))
                    rackBarcodeCol = i;
                else if (BarcodeKeywords.Any(k => h.Contains(k)) && rackBarcodeCol != i)
                    vialBarcodeCol = i;
            }
        }

        // Infer from data patterns if headers didn't help
        if (tareCol == null && numericColumns.Count > 0)
        {
            // Sort numeric columns by their average values
            var avgValues = numericColumns
                .Select(col => new
                {
                    Column = col,
                    Avg = dataRows
                        .Where(r => r.Length > col && decimal.TryParse(r[col], out _))
                        .Select(r => decimal.Parse(r[col]))
                        .DefaultIfEmpty(0)
                        .Average()
                })
                .OrderBy(x => x.Avg)
                .ToList();

            if (numericColumns.Count == 1)
            {
                tareCol = numericColumns[0];
            }
            else if (numericColumns.Count == 2)
            {
                tareCol = avgValues[0].Column;  // Lower = tare
                grossCol = avgValues[1].Column; // Higher = gross
            }
            else if (numericColumns.Count >= 3)
            {
                grossCol = avgValues[^1].Column; // Highest = gross
                tareCol = avgValues[1].Column;   // Middle = tare
                netCol = avgValues[0].Column;    // Lowest = net (could also be highest-middle)
            }
        }

        // Infer barcodes from string columns
        if (vialBarcodeCol == null || rackBarcodeCol == null)
        {
            foreach (var col in stringColumns.OrderBy(c => c))
            {
                if (!uniqueValueCounts.TryGetValue(col, out var uniqueCount)) continue;

                var totalRows = dataRows.Count;

                // Rack barcode: same value across all rows (low uniqueness)
                if (rackBarcodeCol == null && uniqueCount == 1)
                {
                    rackBarcodeCol = col;
                }
                // Vial barcode: unique per row (high uniqueness)
                else if (vialBarcodeCol == null && uniqueCount >= totalRows * 0.9)
                {
                    vialBarcodeCol = col;
                }
            }
        }

        return new CsvColumnMapping
        {
            Headers = headers,
            TareWeightColumn = tareCol,
            GrossWeightColumn = grossCol,
            NetWeightColumn = netCol,
            VialBarcodeColumn = vialBarcodeCol,
            RackBarcodeColumn = rackBarcodeCol
        };
    }

    private static List<FractionDto> ExtractFractions(
        List<string[]> dataRows,
        CsvColumnMapping mapping)
    {
        var fractions = new List<FractionDto>();

        for (int i = 0; i < dataRows.Count; i++)
        {
            var row = dataRows[i];

            fractions.Add(new FractionDto
            {
                RowNumber = i + 1,
                VialBarcode = GetValue(row, mapping.VialBarcodeColumn),
                RackBarcode = GetValue(row, mapping.RackBarcodeColumn),
                TareWeight = GetDecimalValue(row, mapping.TareWeightColumn) ?? 0,
                GrossWeight = GetDecimalValue(row, mapping.GrossWeightColumn),
                NetWeight = GetDecimalValue(row, mapping.NetWeightColumn)
            });
        }

        return fractions;
    }

    private static string? GetValue(string[] row, int? column) =>
        column.HasValue && row.Length > column.Value
            ? row[column.Value].Trim()
            : null;

    private static decimal? GetDecimalValue(string[] row, int? column)
    {
        var value = GetValue(row, column);
        return decimal.TryParse(value, out var result) ? result : null;
    }

    private static string? DetermineRackBarcode(
        List<FractionDto> fractions,
        CsvColumnMapping mapping)
    {
        if (mapping.RackBarcodeColumn.HasValue)
        {
            return fractions
                .Select(f => f.RackBarcode)
                .Where(b => !string.IsNullOrEmpty(b))
                .GroupBy(b => b)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;
        }
        return null;
    }
}
```

---

## 2. Blazor UI Components

### Design Principles

- **Single Page**: All actions on one page, no navigation required
- **Minimal Clicks**: Drag-drop upload, instant preview, one-click confirm
- **Progressive Disclosure**: Show details on demand
- **Visual Feedback**: Clear success/error states, progress indicators

### Main Page: RackAssignment.razor

```razor
@page "/rack-assignment"
@using PPM.Shared.DTOs
@inject ICsvParserService CsvParser
@inject ISnackbar Snackbar

<MudContainer MaxWidth="MaxWidth.Large" Class="py-4">
    <MudText Typo="Typo.h4" Class="mb-4">Rack Assignment</MudText>

    @if (_parseResult == null)
    {
        <CsvDropZone OnFileUploaded="HandleFileUpload" IsLoading="_isLoading" />
    }
    else if (_parseResult.Success)
    {
        <MudPaper Class="pa-4">
            <MudStack Spacing="4">
                @* Header with rack info *@
                <MudStack Row="true" Justify="Justify.SpaceBetween" AlignItems="AlignItems.Center">
                    <MudStack Spacing="0">
                        <MudText Typo="Typo.h6">
                            <MudIcon Icon="@Icons.Material.Filled.ViewModule" Class="mr-2" />
                            @_parseResult.RackAssignment!.RackBarcode
                        </MudText>
                        <MudText Typo="Typo.caption" Color="Color.Secondary">
                            @_parseResult.RackAssignment.TotalFractions fractions detected
                        </MudText>
                    </MudStack>
                    <MudStack Row="true" Spacing="2">
                        <MudButton Variant="Variant.Text"
                                   OnClick="Reset"
                                   StartIcon="@Icons.Material.Filled.Refresh">
                            Start Over
                        </MudButton>
                        <MudButton Variant="Variant.Filled"
                                   Color="Color.Primary"
                                   OnClick="ConfirmAssignment"
                                   StartIcon="@Icons.Material.Filled.Check">
                            Confirm Assignment
                        </MudButton>
                    </MudStack>
                </MudStack>

                <MudDivider />

                @* Column detection summary *@
                <MudAlert Severity="Severity.Info" Dense="true" Class="mb-2">
                    <MudText Typo="Typo.body2">
                        <strong>Auto-detected:</strong>
                        @if (_parseResult.DetectedMapping?.TareWeightColumn != null)
                        {
                            <MudChip T="string" Size="Size.Small" Color="Color.Success">Tare Weight</MudChip>
                        }
                        @if (_parseResult.DetectedMapping?.GrossWeightColumn != null)
                        {
                            <MudChip T="string" Size="Size.Small" Color="Color.Success">Gross Weight</MudChip>
                        }
                        @if (_parseResult.DetectedMapping?.VialBarcodeColumn != null)
                        {
                            <MudChip T="string" Size="Size.Small" Color="Color.Success">Vial Barcode</MudChip>
                        }
                        @if (_parseResult.DetectedMapping?.RackBarcodeColumn != null)
                        {
                            <MudChip T="string" Size="Size.Small" Color="Color.Success">Rack Barcode</MudChip>
                        }
                    </MudText>
                </MudAlert>

                @* Fraction grid *@
                <FractionGrid Fractions="_parseResult.RackAssignment.Fractions" />
            </MudStack>
        </MudPaper>
    }
    else
    {
        <MudAlert Severity="Severity.Error" Class="mb-4">
            @_parseResult.ErrorMessage
            <MudButton Variant="Variant.Text" Color="Color.Error" OnClick="Reset" Class="ml-4">
                Try Again
            </MudButton>
        </MudAlert>
    }
</MudContainer>

@code {
    private bool _isLoading;
    private CsvParseResultDto? _parseResult;

    private async Task HandleFileUpload(IBrowserFile file)
    {
        _isLoading = true;
        StateHasChanged();

        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            _parseResult = await CsvParser.ParseAsync(ms, file.Name);

            if (_parseResult.Success)
            {
                Snackbar.Add($"Successfully parsed {_parseResult.RowsParsed} fractions", Severity.Success);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Upload failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void Reset()
    {
        _parseResult = null;
    }

    private async Task ConfirmAssignment()
    {
        // TODO: Call backend to save rack assignment
        Snackbar.Add("Rack assignment confirmed!", Severity.Success);
    }
}
```

### CsvDropZone.razor

```razor
@using Microsoft.AspNetCore.Components.Forms

<MudPaper Class="pa-8 d-flex flex-column align-center justify-center"
          Style="min-height: 300px; border: 2px dashed var(--mud-palette-primary); cursor: pointer;"
          @ondragenter="@(() => _isDragOver = true)"
          @ondragleave="@(() => _isDragOver = false)"
          @ondragover:preventDefault="true"
          Elevation="@(_isDragOver ? 4 : 0)">

    <InputFile OnChange="HandleFileSelected"
               accept=".csv,.txt,.tsv"
               hidden
               id="csvFileInput" />

    @if (IsLoading)
    {
        <MudProgressCircular Indeterminate="true" Size="Size.Large" />
        <MudText Class="mt-4">Parsing CSV file...</MudText>
    }
    else
    {
        <MudIcon Icon="@Icons.Material.Filled.CloudUpload"
                 Size="Size.Large"
                 Color="@(_isDragOver ? Color.Primary : Color.Default)" />
        <MudText Typo="Typo.h6" Class="mt-4">
            Drop CSV file here
        </MudText>
        <MudText Typo="Typo.body2" Color="Color.Secondary">
            or
        </MudText>
        <MudButton Variant="Variant.Outlined"
                   Color="Color.Primary"
                   Class="mt-2"
                   HtmlTag="label"
                   for="csvFileInput">
            Browse Files
        </MudButton>
        <MudText Typo="Typo.caption" Color="Color.Secondary" Class="mt-4">
            Supports CSV, TSV, and TXT files with comma, tab, or semicolon delimiters
        </MudText>
    }
</MudPaper>

@code {
    [Parameter] public EventCallback<IBrowserFile> OnFileUploaded { get; set; }
    [Parameter] public bool IsLoading { get; set; }

    private bool _isDragOver;

    private async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        if (e.File != null)
        {
            await OnFileUploaded.InvokeAsync(e.File);
        }
    }
}
```

### FractionGrid.razor

```razor
@using PPM.Shared.DTOs

<MudDataGrid Items="Fractions"
             Dense="true"
             Striped="true"
             Bordered="true"
             Filterable="false"
             SortMode="SortMode.None"
             Virtualize="true"
             Height="400px"
             Class="fraction-grid">
    <Columns>
        <PropertyColumn Property="x => x.RowNumber" Title="#" />

        @if (Fractions.Any(f => !string.IsNullOrEmpty(f.VialBarcode)))
        {
            <PropertyColumn Property="x => x.VialBarcode" Title="Vial Barcode">
                <CellTemplate>
                    <MudText Typo="Typo.body2">
                        <code>@context.Item.VialBarcode</code>
                    </MudText>
                </CellTemplate>
            </PropertyColumn>
        }

        <PropertyColumn Property="x => x.TareWeight" Title="Tare (g)" Format="F4">
            <CellTemplate>
                <MudText Typo="Typo.body2" Color="Color.Primary">
                    @context.Item.TareWeight.ToString("F4")
                </MudText>
            </CellTemplate>
        </PropertyColumn>

        @if (Fractions.Any(f => f.GrossWeight.HasValue))
        {
            <PropertyColumn Property="x => x.GrossWeight" Title="Gross (g)" Format="F4" />
        }

        @if (Fractions.Any(f => f.NetWeight.HasValue))
        {
            <PropertyColumn Property="x => x.NetWeight" Title="Net (g)" Format="F4" />
        }
    </Columns>

    <NoRecordsContent>
        <MudText>No fractions found in file</MudText>
    </NoRecordsContent>
</MudDataGrid>

@code {
    [Parameter, EditorRequired]
    public IReadOnlyList<FractionDto> Fractions { get; set; } = [];
}
```

---

## 3. Integration & API

### API Controller

```csharp
// PPM.Server/Controllers/FractionController.cs
using Microsoft.AspNetCore.Mvc;
using PPM.Server.Services;
using PPM.Shared.DTOs;

namespace PPM.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FractionController : ControllerBase
{
    private readonly ICsvParserService _csvParser;
    private readonly IFractionRepository _fractionRepository; // Your existing backend

    public FractionController(
        ICsvParserService csvParser,
        IFractionRepository fractionRepository)
    {
        _csvParser = csvParser;
        _fractionRepository = fractionRepository;
    }

    [HttpPost("parse")]
    public async Task<ActionResult<CsvParseResultDto>> ParseCsv(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file provided");

        await using var stream = file.OpenReadStream();
        var result = await _csvParser.ParseAsync(stream, file.FileName);

        return Ok(result);
    }

    [HttpPost("assign")]
    public async Task<ActionResult> AssignToRack([FromBody] RackAssignmentDto assignment)
    {
        // Save to your existing backend
        await _fractionRepository.SaveRackAssignmentAsync(assignment);
        return Ok();
    }
}
```

### Service Registration

```csharp
// Program.cs additions
builder.Services.AddScoped<ICsvParserService, CsvParserService>();
```

---

## 4. NuGet Dependencies

```xml
<!-- PPM.Server.csproj -->
<PackageReference Include="CsvHelper" Version="31.*" />
<PackageReference Include="MudBlazor" Version="7.*" />

<!-- PPM.Client.csproj -->
<PackageReference Include="MudBlazor" Version="7.*" />

<!-- PPM.Shared.csproj -->
<!-- No additional dependencies needed -->
```

---

## 5. Sample CSV Formats Supported

### Format 1: With Headers
```csv
Rack Barcode,Vial Barcode,Tare Weight,Gross Weight
RACK-001,VL-0001,12.3456,15.6789
RACK-001,VL-0002,12.4567,16.7890
```

### Format 2: No Headers, Weights Only
```csv
12.3456
12.4567
12.5678
```

### Format 3: Mixed Data
```csv
VL-0001,12.3456,15.6789
VL-0002,12.4567,16.7890
```

### Format 4: Three Weight Columns (Gross, Tare, Net)
```csv
Sample,Gross,Tare,Net
A1,25.1234,12.3456,12.7778
A2,26.2345,12.4567,13.7778
```

---

## 6. Testing

### Unit Test Examples

```csharp
[Fact]
public async Task ParseAsync_SingleWeightColumn_DetectsTareWeight()
{
    var csv = "12.3456\n12.4567\n12.5678";
    var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

    var result = await _parser.ParseAsync(stream, "test.csv");

    Assert.True(result.Success);
    Assert.Equal(3, result.RowsParsed);
    Assert.Equal(12.3456m, result.RackAssignment.Fractions[0].TareWeight);
}

[Fact]
public async Task ParseAsync_TwoWeightColumns_DetectsTareAndGross()
{
    var csv = "12.3456,25.6789\n12.4567,26.7890";
    var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

    var result = await _parser.ParseAsync(stream, "test.csv");

    Assert.Equal(12.3456m, result.RackAssignment.Fractions[0].TareWeight);
    Assert.Equal(25.6789m, result.RackAssignment.Fractions[0].GrossWeight);
}

[Fact]
public async Task ParseAsync_WithHeaders_DetectsColumns()
{
    var csv = "Vial,Tare,Gross\nVL001,12.3456,25.6789";
    var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

    var result = await _parser.ParseAsync(stream, "test.csv");

    Assert.True(result.HasHeaders);
    Assert.Equal("VL001", result.RackAssignment.Fractions[0].VialBarcode);
}
```

---

## 7. Future Enhancements

- [ ] Manual column mapping override UI
- [ ] Batch upload multiple racks
- [ ] Export parsed data
- [ ] Rack position visualization (grid layout)
- [ ] Barcode scanner integration
- [ ] Weight validation rules (min/max thresholds)
