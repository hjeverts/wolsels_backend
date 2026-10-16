using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Wolsels.Api.Contracts;

/// <summary>
/// Dieren importeren, bv. uit een CSV-bestand dat de frontend heeft ingelezen. Dieren worden op naam
/// gekoppeld aan bestaande dieren; die worden overgeslagen of (met <see cref="UpdateExisting"/>) bijgewerkt.
/// </summary>
public sealed class AnimalImportRequest : IValidatableObject
{
    public const int MaxRows = 1000;

    /// <summary>Velden die in het bestand staan. Bij bijwerken blijven de andere velden ongewijzigd.</summary>
    public static readonly string[] KnownFields = ["species", "breed", "color", "birthDate", "location", "active", "notes"];

    public List<AnimalImportRow> Rows { get; set; } = [];
    public List<string> Fields { get; set; } = [];
    public bool UpdateExisting { get; set; }
    /// <summary>Diersoort voor nieuwe dieren waarvan de diersoort in het bestand leeg is.</summary>
    public string? DefaultSpecies { get; set; }

    public bool Has(string field) => Fields.Contains(field, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Rows.Count == 0)
            yield return new ValidationResult("Het bestand bevat geen dieren.", [nameof(Rows)]);
        if (Rows.Count > MaxRows)
            yield return new ValidationResult($"Importeer maximaal {MaxRows} dieren tegelijk.", [nameof(Rows)]);
        if (DefaultSpecies?.Trim().Length > 100)
            yield return new ValidationResult("De standaard-diersoort is langer dan 100 tekens.", [nameof(DefaultSpecies)]);
        foreach (var field in Fields.Where(f => !KnownFields.Contains(f, StringComparer.OrdinalIgnoreCase)))
            yield return new ValidationResult($"Onbekend veld: {field}.", [nameof(Fields)]);

        for (var i = 0; i < Rows.Count && i < MaxRows; i++)
        {
            var row = Rows[i];
            if (row is null)
            {
                yield return new ValidationResult($"Regel {i + 1}: dier ontbreekt.", [nameof(Rows)]);
                continue;
            }
            var prefix = $"Regel {row.Line ?? i + 1}: ";
            if (string.IsNullOrWhiteSpace(row.Name))
                yield return new ValidationResult(prefix + "naam ontbreekt.", [nameof(Rows)]);
            if (!row.HasValidBirthDate)
                yield return new ValidationResult($"{prefix}geboortedatum \"{row.BirthDate}\" is geen geldige datum (jjjj-mm-dd).", [nameof(Rows)]);
            foreach (var (value, max, label) in new[]
                     {
                         (row.Name, 200, "naam"), (row.Species, 100, "diersoort"), (row.Breed, 100, "ras"),
                         (row.Color, 100, "kleur"), (row.Location, 200, "locatie"), (row.Notes, 4000, "notities"),
                     })
            {
                if (value?.Trim().Length > max)
                    yield return new ValidationResult($"{prefix}{label} is langer dan {max} tekens.", [nameof(Rows)]);
            }
        }
    }
}

public sealed class AnimalImportRow
{
    /// <summary>Regelnummer in het bestand, voor foutmeldingen.</summary>
    public int? Line { get; set; }
    public string Name { get; set; } = "";
    public string? Species { get; set; }
    public string? Breed { get; set; }
    public string? Color { get; set; }
    /// <summary>
    /// Als tekst (jjjj-mm-dd), zodat een ongeldige datum een fout met het regelnummer geeft in plaats van
    /// een fout bij het inlezen van de JSON.
    /// </summary>
    public string? BirthDate { get; set; }
    /// <summary>Naam van de locatie; een onbekende locatie wordt aangemaakt.</summary>
    public string? Location { get; set; }
    /// <summary>Leeg: een nieuw dier is actief, een bestaand dier blijft ongewijzigd.</summary>
    public bool? Active { get; set; }
    public string? Notes { get; set; }

    [JsonIgnore]
    public DateOnly? BirthDateValue =>
        DateOnly.TryParseExact(BirthDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    [JsonIgnore]
    public bool HasValidBirthDate => string.IsNullOrWhiteSpace(BirthDate) || BirthDateValue is not null;
}

public sealed record AnimalImportResult(int Created, int Updated, int Skipped, IReadOnlyList<string> LocationsCreated);
