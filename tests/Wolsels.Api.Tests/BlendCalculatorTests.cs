using Wolsels.Api.Domain;
using Wolsels.Api.Services;

namespace Wolsels.Api.Tests;

public class BlendCalculatorTests
{
    private readonly BlendCalculator _calculator = new(maxComponents: 4);

    [Fact]
    public void TotalWeight_SplitsByRatio()
    {
        var result = _calculator.Calculate(new([60, 40], CalculationBasis.TotalWeight, 250, null, null));

        Assert.Equal([150m, 100m], result.ComponentWeightsGrams);
        Assert.Equal([60m, 40m], result.Percentages);
        Assert.Equal(250m, result.TotalWeightGrams);
    }

    [Fact]
    public void TotalWeight_PartsAndPercentagesAreEquivalent()
    {
        var parts = _calculator.Calculate(new([3, 2], CalculationBasis.TotalWeight, 250, null, null));
        var percentages = _calculator.Calculate(new([60, 40], CalculationBasis.TotalWeight, 250, null, null));

        Assert.Equal(percentages.ComponentWeightsGrams, parts.ComponentWeightsGrams);
    }

    [Fact]
    public void TotalWeight_RoundedWeightsAlwaysAddUpToTotal()
    {
        var result = _calculator.Calculate(new([1, 1, 1], CalculationBasis.TotalWeight, 100, null, null));

        Assert.Equal([33.4m, 33.3m, 33.3m], result.ComponentWeightsGrams);
        Assert.Equal(100m, result.ComponentWeightsGrams.Sum());
    }

    [Fact]
    public void ComponentWeight_ScalesOtherComponents()
    {
        // 50% merino, 30% alpaca, 20% silk; 120 g of alpaca known.
        var result = _calculator.Calculate(new([50, 30, 20], CalculationBasis.ComponentWeight, null, 1, 120));

        Assert.Equal([200m, 120m, 80m], result.ComponentWeightsGrams);
        Assert.Equal(400m, result.TotalWeightGrams);
    }

    [Fact]
    public void ComponentWeight_KeepsKnownWeightExact()
    {
        var result = _calculator.Calculate(new([1, 3], CalculationBasis.ComponentWeight, null, 1, 100));

        Assert.Equal(100m, result.ComponentWeightsGrams[1]);
        Assert.Equal(33.3m, result.ComponentWeightsGrams[0]);
        Assert.Equal(133.3m, result.TotalWeightGrams);
    }

    [Theory]
    [InlineData(new double[0], "components")]
    [InlineData(new double[] { 1, 1, 1, 1, 1 }, "components")]
    [InlineData(new double[] { 1, 0 }, "ratio")]
    [InlineData(new double[] { 1, -2 }, "ratio")]
    public void InvalidRatios_AreRejected(double[] ratios, string field)
    {
        var ex = Assert.Throws<BlendCalculationException>(() => _calculator.Calculate(
            new(ratios.Select(r => (decimal)r).ToList(), CalculationBasis.TotalWeight, 100, null, null)));
        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void MissingTotal_IsRejected()
    {
        var ex = Assert.Throws<BlendCalculationException>(() =>
            _calculator.Calculate(new([1, 1], CalculationBasis.TotalWeight, null, null, null)));
        Assert.Equal("totalWeightGrams", ex.Field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(2)]
    public void InvalidBasisComponent_IsRejected(int? index)
    {
        var ex = Assert.Throws<BlendCalculationException>(() =>
            _calculator.Calculate(new([1, 1], CalculationBasis.ComponentWeight, null, index, 50)));
        Assert.Equal("basisComponentIndex", ex.Field);
    }
}
