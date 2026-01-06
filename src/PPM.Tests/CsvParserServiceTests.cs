using System.Text;
using FluentAssertions;
using PPM.Server.Services;
using PPM.Shared.DTOs;

namespace PPM.Tests;

public class CsvParserServiceTests
{
    private readonly CsvParserService _parser = new();

    private static Stream ToStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    #region Single Weight Column Tests

    [Fact]
    public async Task ParseAsync_SingleWeightColumn_DetectsTareWeight()
    {
        // Arrange
        var csv = "12.3456\n12.4567\n12.5678";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.RowsParsed.Should().Be(3);
        result.HasHeaders.Should().BeFalse();
        result.DetectedMapping!.TareWeightColumn.Should().Be(0);
        result.RackAssignment!.Fractions.Should().HaveCount(3);
        result.RackAssignment.Fractions[0].TareWeight.Should().Be(12.3456m);
        result.RackAssignment.Fractions[1].TareWeight.Should().Be(12.4567m);
        result.RackAssignment.Fractions[2].TareWeight.Should().Be(12.5678m);
    }

    [Fact]
    public async Task ParseAsync_SingleWeightWithVialBarcode_DetectsBoth()
    {
        // Arrange
        var csv = "VL-001,12.3456\nVL-002,12.4567\nVL-003,12.5678";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.DetectedMapping!.VialBarcodeColumn.Should().Be(0);
        result.DetectedMapping.TareWeightColumn.Should().Be(1);
        result.RackAssignment!.Fractions[0].VialBarcode.Should().Be("VL-001");
        result.RackAssignment.Fractions[0].TareWeight.Should().Be(12.3456m);
    }

    #endregion

    #region Two Weight Column Tests

    [Fact]
    public async Task ParseAsync_TwoWeightColumns_DetectsTareAndGross()
    {
        // Arrange - lower value is tare, higher is gross
        var csv = "12.3456,25.6789\n12.4567,26.7890\n12.5678,27.8901";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.DetectedMapping!.TareWeightColumn.Should().NotBeNull();
        result.DetectedMapping.GrossWeightColumn.Should().NotBeNull();

        var firstFraction = result.RackAssignment!.Fractions[0];
        firstFraction.TareWeight.Should().Be(12.3456m);
        firstFraction.GrossWeight.Should().Be(25.6789m);
    }

    [Fact]
    public async Task ParseAsync_TwoWeightColumnsWithBarcode_DetectsAll()
    {
        // Arrange
        var csv = "VL-001,12.3456,25.6789\nVL-002,12.4567,26.7890";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.DetectedMapping!.VialBarcodeColumn.Should().Be(0);
        result.RackAssignment!.HasVialBarcodes.Should().BeTrue();
    }

    #endregion

    #region Three Weight Column Tests

    [Fact]
    public async Task ParseAsync_ThreeWeightColumns_DetectsMiddleAsTare()
    {
        // Arrange - Pattern: Gross (highest), Tare (middle), Net (lowest)
        var csv = "25.0000,12.0000,13.0000\n26.0000,12.5000,13.5000";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.DetectedMapping!.GrossWeightColumn.Should().NotBeNull();
        result.DetectedMapping.TareWeightColumn.Should().NotBeNull();

        // The tare should be the middle value (~12)
        var firstFraction = result.RackAssignment!.Fractions[0];
        firstFraction.TareWeight.Should().BeInRange(11m, 13m);
    }

    #endregion

    #region Header Detection Tests

    [Fact]
    public async Task ParseAsync_WithHeaders_DetectsHeaderRow()
    {
        // Arrange
        var csv = "Vial Barcode,Tare Weight,Gross Weight\nVL001,12.3456,25.6789\nVL002,12.4567,26.7890";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.HasHeaders.Should().BeTrue();
        result.RowsParsed.Should().Be(2); // Excludes header
        result.DetectedMapping!.Headers.Should().Contain("Vial Barcode");
    }

    [Fact]
    public async Task ParseAsync_WithTareGrossHeaders_MapsCorrectly()
    {
        // Arrange
        var csv = "Sample,Tare,Gross\nA1,12.0,25.0\nA2,12.5,26.0";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.HasHeaders.Should().BeTrue();
        result.DetectedMapping!.TareWeightColumn.Should().Be(1);
        result.DetectedMapping.GrossWeightColumn.Should().Be(2);
    }

    [Fact]
    public async Task ParseAsync_WithRackBarcodeHeader_DetectsRack()
    {
        // Arrange
        var csv = "Rack,Vial,Tare\nRACK-001,VL001,12.0\nRACK-001,VL002,12.5";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.DetectedMapping!.RackBarcodeColumn.Should().Be(0);
        result.RackAssignment!.RackBarcode.Should().Be("RACK-001");
    }

    #endregion

    #region Rack Barcode Detection Tests

    [Fact]
    public async Task ParseAsync_RepeatingRackBarcode_DetectsAsRack()
    {
        // Arrange - All rows have same rack barcode
        var csv = "RACK-001,VL001,12.0\nRACK-001,VL002,12.5\nRACK-001,VL003,13.0";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.DetectedMapping!.RackBarcodeColumn.Should().Be(0);
        result.DetectedMapping.VialBarcodeColumn.Should().Be(1);
        result.RackAssignment!.RackBarcode.Should().Be("RACK-001");
    }

    [Fact]
    public async Task ParseAsync_NoRackBarcode_UsesFilename()
    {
        // Arrange
        var csv = "VL001,12.0\nVL002,12.5";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "MyRack123.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.RackAssignment!.RackBarcode.Should().Be("MyRack123");
    }

    #endregion

    #region Delimiter Detection Tests

    [Fact]
    public async Task ParseAsync_TabDelimited_ParsesCorrectly()
    {
        // Arrange
        var csv = "VL001\t12.3456\nVL002\t12.4567";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.tsv");

        // Assert
        result.Success.Should().BeTrue();
        result.RackAssignment!.Fractions.Should().HaveCount(2);
        result.RackAssignment.Fractions[0].VialBarcode.Should().Be("VL001");
    }

    [Fact]
    public async Task ParseAsync_SemicolonDelimited_ParsesCorrectly()
    {
        // Arrange
        var csv = "VL001;12.3456\nVL002;12.4567";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.RackAssignment!.Fractions[0].VialBarcode.Should().Be("VL001");
        result.RackAssignment.Fractions[0].TareWeight.Should().Be(12.3456m);
    }

    #endregion

    #region Quoted Fields Tests

    [Fact]
    public async Task ParseAsync_QuotedFields_ParsesCorrectly()
    {
        // Arrange
        var csv = "\"VL-001\",12.3456\n\"VL-002\",12.4567";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.RackAssignment!.Fractions[0].VialBarcode.Should().Be("VL-001");
    }

    [Fact]
    public async Task ParseAsync_QuotedFieldsWithCommas_ParsesCorrectly()
    {
        // Arrange
        var csv = "\"Sample, A1\",12.3456\n\"Sample, A2\",12.4567";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.RackAssignment!.Fractions[0].VialBarcode.Should().Be("Sample, A1");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ParseAsync_EmptyFile_ReturnsError()
    {
        // Arrange
        var csv = "";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "empty.csv");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task ParseAsync_HeadersOnly_ReturnsError()
    {
        // Arrange
        var csv = "Vial,Tare,Gross";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "headers.csv");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No data rows");
    }

    [Fact]
    public async Task ParseAsync_EuropeanNumberFormat_ParsesCorrectly()
    {
        // Arrange - European format uses comma as decimal separator
        var csv = "VL001;12,3456\nVL002;12,4567";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.RackAssignment!.Fractions[0].TareWeight.Should().Be(12.3456m);
    }

    [Fact]
    public async Task ParseAsync_RowNumbers_AreOneIndexed()
    {
        // Arrange
        var csv = "12.0\n12.5\n13.0";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "test.csv");

        // Assert
        result.RackAssignment!.Fractions[0].RowNumber.Should().Be(1);
        result.RackAssignment.Fractions[1].RowNumber.Should().Be(2);
        result.RackAssignment.Fractions[2].RowNumber.Should().Be(3);
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public async Task ParseAsync_TypicalHplcOutput_ParsesCorrectly()
    {
        // Arrange - Typical HPLC fraction collector output
        var csv = @"Rack Barcode,Fraction ID,Tare Weight (g),Gross Weight (g)
HPLC-RACK-2024-001,F001,12.3456,25.7891
HPLC-RACK-2024-001,F002,12.4123,24.8765
HPLC-RACK-2024-001,F003,12.3890,26.1234
HPLC-RACK-2024-001,F004,12.4567,25.9012";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "hplc_run.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.HasHeaders.Should().BeTrue();
        result.RowsParsed.Should().Be(4);
        result.RackAssignment!.RackBarcode.Should().Be("HPLC-RACK-2024-001");
        result.RackAssignment.HasVialBarcodes.Should().BeTrue();
        result.RackAssignment.HasGrossWeights.Should().BeTrue();

        var firstFraction = result.RackAssignment.Fractions[0];
        firstFraction.VialBarcode.Should().Be("F001");
        firstFraction.TareWeight.Should().Be(12.3456m);
        firstFraction.GrossWeight.Should().Be(25.7891m);
    }

    [Fact]
    public async Task ParseAsync_MinimalWeightsOnly_ParsesCorrectly()
    {
        // Arrange - Just tare weights from a balance export
        var csv = @"12.3456
12.4123
12.3890
12.4567
12.3999";

        // Act
        var result = await _parser.ParseAsync(ToStream(csv), "balance_export.csv");

        // Assert
        result.Success.Should().BeTrue();
        result.HasHeaders.Should().BeFalse();
        result.RowsParsed.Should().Be(5);
        result.RackAssignment!.RackBarcode.Should().Be("balance_export");
        result.RackAssignment.Fractions.Should().AllSatisfy(f => f.TareWeight.Should().BeGreaterThan(0));
    }

    #endregion
}
