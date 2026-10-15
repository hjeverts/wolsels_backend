namespace Wolsels.Api.Domain;

/// <summary>Een partij wol op voorraad.</summary>
public class Wool : IOwned
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public required string Name { get; set; }
    /// <summary>Wolsoort, bv. Merino, Shetland, Alpaca.</summary>
    public required string WoolType { get; set; }
    public required string Color { get; set; }
    /// <summary>Optionele kleurcode (#rrggbb) voor weergave.</summary>
    public string? ColorHex { get; set; }
    /// <summary>Huidig gewicht op voorraad in gram.</summary>
    public decimal WeightGrams { get; set; }
    public string? Origin { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
