using System.ComponentModel.DataAnnotations;
using Wolsels.Api.Domain;

namespace Wolsels.Api.Contracts;

public sealed record BlendComponentDto(
    int WoolId,
    string WoolName,
    string WoolType,
    string Color,
    string? ColorHex,
    decimal Ratio,
    decimal Percentage,
    decimal WeightGrams);

public sealed record BlendDto(
    int Id,
    string Name,
    BlendStatus Status,
    bool StockDeducted,
    CalculationBasis Basis,
    int? BasisComponentIndex,
    decimal InputWeightGrams,
    string? Notes,
    IReadOnlyList<BlendComponentDto> Components,
    DateOnly? SpunOn,
    decimal? OutputWeightGrams,
    decimal? YieldPercentage,
    decimal? OutputLengthMeters,
    string? YarnWeight,
    int? WrapsPerInch,
    int? PlyCount,
    string? PlyMethod,
    string? SpinDirection,
    bool IsDyed,
    string? DyeColor,
    string? DyeColorHex,
    string? DyeProduct,
    string? DyeMethod,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static BlendDto From(Blend b)
    {
        var components = b.Components.OrderBy(c => c.Position).ToList();
        var ratioSum = components.Sum(c => c.Ratio);
        return new BlendDto(
            b.Id, b.Name, b.Status, b.StockDeducted, b.Basis, b.BasisComponentIndex, b.InputWeightGrams, b.Notes,
            components.Select(c => new BlendComponentDto(
                c.WoolId,
                c.Wool?.Name ?? "",
                c.Wool?.WoolType ?? "",
                c.Wool?.Color ?? "",
                c.Wool?.ColorHex,
                c.Ratio,
                ratioSum > 0 ? Math.Round(c.Ratio / ratioSum * 100m, 1) : 0,
                c.WeightGrams)).ToList(),
            b.SpunOn,
            b.OutputWeightGrams,
            b.OutputWeightGrams is { } output && b.InputWeightGrams > 0
                ? Math.Round(output / b.InputWeightGrams * 100m, 1)
                : null,
            b.OutputLengthMeters, b.YarnWeight, b.WrapsPerInch, b.PlyCount, b.PlyMethod, b.SpinDirection,
            b.IsDyed, b.DyeColor, b.DyeColorHex, b.DyeProduct, b.DyeMethod,
            b.CreatedAt, b.UpdatedAt);
    }
}

public sealed class BlendComponentRequest
{
    [Range(1, int.MaxValue)] public int WoolId { get; set; }
    [Range(0.0001, 1_000_000)] public decimal Ratio { get; set; }
}

public sealed class BlendRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    public BlendStatus Status { get; set; } = BlendStatus.Planned;
    [MaxLength(4000)] public string? Notes { get; set; }

    [Required, MinLength(1)] public List<BlendComponentRequest> Components { get; set; } = [];
    public CalculationBasis Basis { get; set; }
    public decimal? TotalWeightGrams { get; set; }
    public int? BasisComponentIndex { get; set; }
    public decimal? BasisComponentWeightGrams { get; set; }

    public DateOnly? SpunOn { get; set; }
    [Range(0, 1_000_000)] public decimal? OutputWeightGrams { get; set; }
    [Range(0, 1_000_000)] public decimal? OutputLengthMeters { get; set; }
    [MaxLength(50)] public string? YarnWeight { get; set; }
    [Range(1, 200)] public int? WrapsPerInch { get; set; }
    [Range(1, 20)] public int? PlyCount { get; set; }
    [MaxLength(100)] public string? PlyMethod { get; set; }
    [RegularExpression("^[SZ]$", ErrorMessage = "Validation.SpinDirection")]
    public string? SpinDirection { get; set; }

    public bool IsDyed { get; set; }
    [MaxLength(100)] public string? DyeColor { get; set; }
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Validation.ColorHex")]
    public string? DyeColorHex { get; set; }
    [MaxLength(200)] public string? DyeProduct { get; set; }
    [MaxLength(4000)] public string? DyeMethod { get; set; }
}

public sealed class BlendCalculationRequest
{
    [Required, MinLength(1)] public List<decimal> Ratios { get; set; } = [];
    public CalculationBasis Basis { get; set; }
    public decimal? TotalWeightGrams { get; set; }
    public int? BasisComponentIndex { get; set; }
    public decimal? BasisComponentWeightGrams { get; set; }
}

public sealed record SettingsDto(int MaxBlendComponents);
