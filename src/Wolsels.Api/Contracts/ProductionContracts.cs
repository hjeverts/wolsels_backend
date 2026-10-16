using System.ComponentModel.DataAnnotations;
using Wolsels.Api.Domain;
using Wolsels.Api.Localization;

namespace Wolsels.Api.Contracts;

public sealed record LocationDto(int Id, string Name, string? Notes, int AnimalCount)
{
    public static LocationDto From(Location l, int animalCount) => new(l.Id, l.Name, l.Notes, animalCount);
}

public sealed class LocationRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(4000)] public string? Notes { get; set; }

    public void ApplyTo(Location l)
    {
        l.Name = Name.Trim();
        l.Notes = Notes.NullIfEmpty();
    }
}

public sealed record AnimalDto(
    int Id,
    string Name,
    string Species,
    string? Breed,
    string? Color,
    DateOnly? BirthDate,
    int? LocationId,
    string? LocationName,
    bool Active,
    string? Notes,
    decimal TotalHarvestedGrams,
    DateOnly? LastHarvestDate)
{
    public static AnimalDto From(Animal a, decimal totalHarvested, DateOnly? lastHarvest) => new(
        a.Id, a.Name, a.Species, a.Breed, a.Color, a.BirthDate, a.LocationId, a.Location?.Name,
        a.Active, a.Notes, totalHarvested, lastHarvest);
}

public sealed class AnimalRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    [Required, MaxLength(100)] public string Species { get; set; } = "Konijn";
    [MaxLength(100)] public string? Breed { get; set; }
    [MaxLength(100)] public string? Color { get; set; }
    public DateOnly? BirthDate { get; set; }
    public int? LocationId { get; set; }
    public bool Active { get; set; } = true;
    [MaxLength(4000)] public string? Notes { get; set; }

    public void ApplyTo(Animal a)
    {
        a.Name = Name.Trim();
        a.Species = Species.Trim();
        a.Breed = Breed.NullIfEmpty();
        a.Color = Color.NullIfEmpty();
        a.BirthDate = BirthDate;
        a.LocationId = LocationId;
        a.Active = Active;
        a.Notes = Notes.NullIfEmpty();
    }
}

public sealed record HarvestDto(
    int Id,
    DateOnly Date,
    int? AnimalId,
    string? AnimalName,
    int? LocationId,
    string? LocationName,
    decimal WeightGrams,
    string? Method,
    string? Grade,
    int? WoolId,
    string? WoolName,
    string? Notes)
{
    public static HarvestDto From(Harvest h) => new(
        h.Id, h.Date, h.AnimalId, h.Animal?.Name, h.LocationId, h.Location?.Name,
        h.WeightGrams, h.Method, h.Grade, h.WoolId, h.Wool?.Name, h.Notes);
}

public sealed class HarvestRequest : IValidatableObject
{
    public DateOnly Date { get; set; }
    public int? AnimalId { get; set; }
    public int? LocationId { get; set; }
    [Range(0.1, 1_000_000)] public decimal WeightGrams { get; set; }
    [MaxLength(100)] public string? Method { get; set; }
    [MaxLength(100)] public string? Grade { get; set; }
    [MaxLength(4000)] public string? Notes { get; set; }
    /// <summary>
    /// Voeg de oogst bij het opslaan toe aan de wolvoorraad, als dat nog niet gebeurd is: bij een bestaande partij
    /// (<see cref="StockWoolId"/>) of als nieuwe partij.
    /// </summary>
    public bool AddToStock { get; set; }

    /// <summary>Bestaande partij waar de oogst bij wordt geteld; leeg = nieuwe partij.</summary>
    public int? StockWoolId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AnimalId is null == LocationId is null)
            yield return new ValidationResult(
                Texts.Get("Harvests.ChooseSource"), [nameof(AnimalId), nameof(LocationId)]);
        if (Date == default)
            yield return new ValidationResult(Texts.Get("Harvests.DateRequired"), [nameof(Date)]);
    }

    public void ApplyTo(Harvest h)
    {
        h.Date = Date;
        h.AnimalId = AnimalId;
        h.LocationId = LocationId;
        h.WeightGrams = WeightGrams;
        h.Method = Method.NullIfEmpty();
        h.Grade = Grade.NullIfEmpty();
        h.Notes = Notes.NullIfEmpty();
    }
}

public sealed record ProductionSourceDto(string Kind, int Id, string Name, decimal TotalGrams, int HarvestCount);

public sealed record ProductionPeriodDto(int Year, int Month, decimal TotalGrams, int HarvestCount);

public sealed record ProductionSummaryDto(
    decimal TotalGrams,
    int HarvestCount,
    IReadOnlyList<ProductionPeriodDto> PerMonth,
    IReadOnlyList<ProductionSourceDto> PerSource);
