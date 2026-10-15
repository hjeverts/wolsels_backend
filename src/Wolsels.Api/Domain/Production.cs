namespace Wolsels.Api.Domain;

/// <summary>Een locatie (stal, hok, weide) waar dieren gehouden worden.</summary>
public class Location : IHouseholdData
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Een wolproducerend dier, bv. een angorakonijn.</summary>
public class Animal : IHouseholdData
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    /// <summary>Diersoort, bv. Konijn, Schaap, Alpaca.</summary>
    public required string Species { get; set; }
    public string? Breed { get; set; }
    public string? Color { get; set; }
    public DateOnly? BirthDate { get; set; }
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    /// <summary>Niet-actieve dieren (overleden, verkocht) blijven bewaard voor de historie.</summary>
    public bool Active { get; set; } = true;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Een oogst van ruwe wol. Wordt geboekt op een dier, of op een locatie wanneer er geen
/// afzonderlijke dieren worden bijgehouden.
/// </summary>
public class Harvest : IHouseholdData
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public DateOnly Date { get; set; }
    public int? AnimalId { get; set; }
    public Animal? Animal { get; set; }
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    /// <summary>Gewicht ruwe wol in gram.</summary>
    public decimal WeightGrams { get; set; }
    /// <summary>Oogstmethode, bv. plukken, scheren, kammen.</summary>
    public string? Method { get; set; }
    /// <summary>Kwaliteit/sortering, bv. 1e keus, 2e keus, vilt.</summary>
    public string? Grade { get; set; }
    /// <summary>Wolvoorraad die bij deze oogst is aangemaakt.</summary>
    public int? WoolId { get; set; }
    public Wool? Wool { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
