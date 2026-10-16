namespace Wolsels.Api.Domain;

/// <summary>A location (shed, hutch, pasture) where animals are kept.</summary>
public class Location : IHouseholdData
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A wool-producing animal, e.g. an Angora rabbit.</summary>
public class Animal : IHouseholdData
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    /// <summary>Species, e.g. Rabbit, Sheep, Alpaca.</summary>
    public required string Species { get; set; }
    public string? Breed { get; set; }
    public string? Color { get; set; }
    public DateOnly? BirthDate { get; set; }
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    /// <summary>Inactive animals (deceased, sold) are kept for the history.</summary>
    public bool Active { get; set; } = true;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A harvest of raw wool. Recorded on an animal, or on a location when individual animals
/// are not tracked.
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
    /// <summary>Raw wool weight in grams.</summary>
    public decimal WeightGrams { get; set; }
    /// <summary>Harvest method, e.g. plucking, shearing, combing.</summary>
    public string? Method { get; set; }
    /// <summary>Quality/grade, e.g. 1st grade, 2nd grade, felted.</summary>
    public string? Grade { get; set; }
    /// <summary>Wool stock created for this harvest.</summary>
    public int? WoolId { get; set; }
    public Wool? Wool { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
