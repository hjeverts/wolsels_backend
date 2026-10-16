namespace Wolsels.Api.Domain;

/// <summary>Een partij wol op voorraad.</summary>
public class Wool : IHouseholdData
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
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

/// <summary>
/// Een foto van een partij wol. De browser verkleint de foto vóór het uploaden; de server bewaart een
/// JPEG (<see cref="Data"/>) en een kleine versie voor lijsten (<see cref="Thumbnail"/>). Wordt samen met de
/// partij verwijderd.
/// </summary>
public class WoolPhoto : IHouseholdData
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public int WoolId { get; set; }
    public required byte[] Data { get; set; }
    public required byte[] Thumbnail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
