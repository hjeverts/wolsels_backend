using Wolsels.Api.Domain;

namespace Wolsels.Api.Services;

public sealed record BlendCalculationInput(
    IReadOnlyList<decimal> Ratios,
    CalculationBasis Basis,
    decimal? TotalWeightGrams,
    int? BasisComponentIndex,
    decimal? BasisComponentWeightGrams);

public sealed record BlendCalculationResult(
    IReadOnlyList<decimal> ComponentWeightsGrams,
    IReadOnlyList<decimal> Percentages,
    decimal TotalWeightGrams);

public class BlendCalculationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>
/// Rekent een mengverhouding om naar gewichten. De verhouding mag in delen (3:2) of procenten (60:40)
/// worden opgegeven; hij wordt altijd genormaliseerd. Gewichten worden afgerond op 0,1 gram.
/// </summary>
/// <remarks>De frontend bevat een identieke implementatie voor directe feedback tijdens het invullen.</remarks>
public class BlendCalculator(int maxComponents)
{
    private const int TenthsPerGram = 10;

    public BlendCalculationResult Calculate(BlendCalculationInput input)
    {
        var ratios = input.Ratios;
        if (ratios.Count == 0)
            throw new BlendCalculationException("components", "Een mengsel heeft minimaal één soort wol nodig.");
        if (ratios.Count > maxComponents)
            throw new BlendCalculationException("components", $"Een mengsel mag maximaal {maxComponents} soorten wol bevatten.");
        if (ratios.Any(r => r <= 0))
            throw new BlendCalculationException("ratio", "Elke verhouding moet groter dan 0 zijn.");

        var ratioSum = ratios.Sum();
        var percentages = ratios.Select(r => Math.Round(r / ratioSum * 100m, 1, MidpointRounding.AwayFromZero)).ToList();

        return input.Basis switch
        {
            CalculationBasis.TotalWeight => FromTotal(ratios, ratioSum, percentages, input.TotalWeightGrams),
            CalculationBasis.ComponentWeight => FromComponent(ratios, percentages, input.BasisComponentIndex, input.BasisComponentWeightGrams),
            _ => throw new BlendCalculationException("basis", "Onbekende rekenbasis."),
        };
    }

    private static BlendCalculationResult FromTotal(
        IReadOnlyList<decimal> ratios, decimal ratioSum, List<decimal> percentages, decimal? totalWeight)
    {
        if (totalWeight is not > 0)
            throw new BlendCalculationException("totalWeightGrams", "Vul een totaalgewicht groter dan 0 in.");

        // Grootste-restmethode: verdeel het totaal in tienden van een gram zodat de som exact klopt.
        var totalTenths = (long)Math.Round(totalWeight.Value * TenthsPerGram, MidpointRounding.AwayFromZero);
        var exact = ratios.Select(r => totalTenths * r / ratioSum).ToList();
        var tenths = exact.Select(e => (long)Math.Floor(e)).ToArray();
        var remainder = totalTenths - tenths.Sum();

        foreach (var index in exact
                     .Select((value, index) => (fraction: value - Math.Floor(value), index))
                     .OrderByDescending(x => x.fraction)
                     .ThenBy(x => x.index)
                     .Take((int)remainder)
                     .Select(x => x.index))
        {
            tenths[index]++;
        }

        var weights = tenths.Select(t => (decimal)t / TenthsPerGram).ToList();
        return new BlendCalculationResult(weights, percentages, (decimal)totalTenths / TenthsPerGram);
    }

    private static BlendCalculationResult FromComponent(
        IReadOnlyList<decimal> ratios, List<decimal> percentages, int? basisIndex, decimal? basisWeight)
    {
        if (basisIndex is not { } index || index < 0 || index >= ratios.Count)
            throw new BlendCalculationException("basisComponentIndex", "Kies de soort wol waarvan het gewicht bekend is.");
        if (basisWeight is not > 0)
            throw new BlendCalculationException("basisComponentWeightGrams", "Vul een gewicht groter dan 0 in.");

        var known = Math.Round(basisWeight.Value, 1, MidpointRounding.AwayFromZero);
        var weights = ratios
            .Select((r, i) => i == index
                ? known
                : Math.Round(known * r / ratios[index], 1, MidpointRounding.AwayFromZero))
            .ToList();

        return new BlendCalculationResult(weights, percentages, weights.Sum());
    }
}
