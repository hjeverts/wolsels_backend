using System.ComponentModel.DataAnnotations;
using Wolsels.Api.Domain;

namespace Wolsels.Api.Contracts;

public sealed record WoolDto(
    int Id,
    string Name,
    string WoolType,
    string Color,
    string? ColorHex,
    decimal WeightGrams,
    string? Origin,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static WoolDto From(Wool w) => new(
        w.Id, w.Name, w.WoolType, w.Color, w.ColorHex, w.WeightGrams, w.Origin, w.Notes, w.CreatedAt, w.UpdatedAt);
}

public sealed class WoolRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    [Required, MaxLength(100)] public string WoolType { get; set; } = "";
    [Required, MaxLength(100)] public string Color { get; set; } = "";
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Gebruik een kleurcode als #a1b2c3.")]
    public string? ColorHex { get; set; }
    [Range(0, 1_000_000)] public decimal WeightGrams { get; set; }
    [MaxLength(200)] public string? Origin { get; set; }
    [MaxLength(4000)] public string? Notes { get; set; }

    public void ApplyTo(Wool w)
    {
        w.Name = Name.Trim();
        w.WoolType = WoolType.Trim();
        w.Color = Color.Trim();
        w.ColorHex = string.IsNullOrWhiteSpace(ColorHex) ? null : ColorHex.ToLowerInvariant();
        w.WeightGrams = WeightGrams;
        w.Origin = Origin.NullIfEmpty();
        w.Notes = Notes.NullIfEmpty();
    }
}

public sealed class MergeWoolRequest
{
    [Range(1, int.MaxValue)] public int TargetWoolId { get; set; }
}

internal static class StringExtensions
{
    public static string? NullIfEmpty(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
