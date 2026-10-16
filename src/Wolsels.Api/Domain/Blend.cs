namespace Wolsels.Api.Domain;

public enum CalculationBasis
{
    /// <summary>Weights are calculated from the total input weight.</summary>
    TotalWeight,
    /// <summary>Weights are calculated from the weight of one component.</summary>
    ComponentWeight,
}

public enum BlendStatus
{
    Planned,
    Spun,
}

/// <summary>A blend of several wools in a fixed ratio.</summary>
public class Blend : IHouseholdData
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public BlendStatus Status { get; set; }
    /// <summary>Whether the component weights have been deducted from the wool stock.</summary>
    public bool StockDeducted { get; set; }
    public CalculationBasis Basis { get; set; }
    /// <summary>Index (0-based) of the component the calculation is based on with <see cref="CalculationBasis.ComponentWeight"/>.</summary>
    public int? BasisComponentIndex { get; set; }
    /// <summary>Total input weight in grams (sum of the components).</summary>
    public decimal InputWeightGrams { get; set; }

    // --- Result after spinning ---

    /// <summary>Date the blend was spun.</summary>
    public DateOnly? SpunOn { get; set; }
    /// <summary>Yield: actual output weight in grams after spinning.</summary>
    public decimal? OutputWeightGrams { get; set; }
    /// <summary>Length of the yarn in metres.</summary>
    public decimal? OutputLengthMeters { get; set; }
    /// <summary>Yarn weight as a category, e.g. Lace, Fingering, DK, Worsted, Bulky.</summary>
    public string? YarnWeight { get; set; }
    /// <summary>Wraps per inch: measured thickness of the yarn.</summary>
    public int? WrapsPerInch { get; set; }
    /// <summary>Number of plies (1 = singles).</summary>
    public int? PlyCount { get; set; }
    /// <summary>Plying method, e.g. 2-ply, chain-plied (Navajo), cabled.</summary>
    public string? PlyMethod { get; set; }
    /// <summary>Spin direction of the singles: S or Z (plying goes in the opposite direction).</summary>
    public string? SpinDirection { get; set; }

    // --- Dyeing ---

    public bool IsDyed { get; set; }
    public string? DyeColor { get; set; }
    public string? DyeColorHex { get; set; }
    /// <summary>Dye used, e.g. acid dye, madder, indigo.</summary>
    public string? DyeProduct { get; set; }
    /// <summary>Dyeing method (recipe, temperature, time, mordanting, ...).</summary>
    public string? DyeMethod { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<BlendComponent> Components { get; set; } = [];
}

public class BlendComponent
{
    public int Id { get; set; }
    public int BlendId { get; set; }
    public int Position { get; set; }
    public int WoolId { get; set; }
    public Wool? Wool { get; set; }
    /// <summary>Share in the ratio (parts or percentages, normalised).</summary>
    public decimal Ratio { get; set; }
    public decimal WeightGrams { get; set; }
}
