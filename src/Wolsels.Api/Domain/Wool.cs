namespace Wolsels.Api.Domain;

/// <summary>A lot of wool in stock.</summary>
public class Wool : IHouseholdData
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    /// <summary>Wool type, e.g. Merino, Shetland, Alpaca.</summary>
    public required string WoolType { get; set; }
    public required string Color { get; set; }
    /// <summary>Optional colour code (#rrggbb) for display.</summary>
    public string? ColorHex { get; set; }
    /// <summary>Current weight in stock in grams.</summary>
    public decimal WeightGrams { get; set; }
    public string? Origin { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A photo of a lot of wool. The browser resizes the photo before uploading; the server stores a
/// JPEG (<see cref="Data"/>) and a small version for lists (<see cref="Thumbnail"/>). Deleted together with the
/// lot.
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
