using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using Wolsels.Api.Localization;

namespace Wolsels.Api.Contracts;

/// <summary>
/// Import animals, e.g. from a CSV file read by the frontend. Animals are matched by name
/// to existing animals; those are skipped or (with <see cref="UpdateExisting"/>) updated.
/// </summary>
public sealed class AnimalImportRequest : IValidatableObject
{
    public const int MaxRows = 1000;

    /// <summary>Fields present in the file. When updating, the other fields stay unchanged.</summary>
    public static readonly string[] KnownFields = ["species", "breed", "color", "birthDate", "location", "active", "notes"];

    public List<AnimalImportRow> Rows { get; set; } = [];
    public List<string> Fields { get; set; } = [];
    public bool UpdateExisting { get; set; }
    /// <summary>Species for new animals whose species is empty in the file.</summary>
    public string? DefaultSpecies { get; set; }

    public bool Has(string field) => Fields.Contains(field, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Rows.Count == 0)
            yield return new ValidationResult(Texts.Get("Import.NoRows"), [nameof(Rows)]);
        if (Rows.Count > MaxRows)
            yield return new ValidationResult(Texts.Get("Import.TooManyRows", MaxRows), [nameof(Rows)]);
        if (DefaultSpecies?.Trim().Length > 100)
            yield return new ValidationResult(Texts.Get("Import.DefaultSpeciesTooLong"), [nameof(DefaultSpecies)]);
        foreach (var field in Fields.Where(f => !KnownFields.Contains(f, StringComparer.OrdinalIgnoreCase)))
            yield return new ValidationResult(Texts.Get("Import.UnknownField", field), [nameof(Fields)]);

        for (var i = 0; i < Rows.Count && i < MaxRows; i++)
        {
            var row = Rows[i];
            if (row is null)
            {
                yield return new ValidationResult(Texts.Get("Import.LinePrefix", i + 1) + Texts.Get("Import.RowMissing"), [nameof(Rows)]);
                continue;
            }
            var prefix = Texts.Get("Import.LinePrefix", row.Line ?? i + 1);
            if (string.IsNullOrWhiteSpace(row.Name))
                yield return new ValidationResult(prefix + Texts.Get("Import.NameMissing"), [nameof(Rows)]);
            if (!row.HasValidBirthDate)
                yield return new ValidationResult(prefix + Texts.Get("Import.InvalidBirthDate", row.BirthDate), [nameof(Rows)]);
            foreach (var (value, max, label) in new[]
                     {
                         (row.Name, 200, "Import.FieldName"), (row.Species, 100, "Import.FieldSpecies"),
                         (row.Breed, 100, "Import.FieldBreed"), (row.Color, 100, "Import.FieldColor"),
                         (row.Location, 200, "Import.FieldLocation"), (row.Notes, 4000, "Import.FieldNotes"),
                     })
            {
                if (value?.Trim().Length > max)
                    yield return new ValidationResult(prefix + Texts.Get("Import.TooLong", Texts.Get(label), max), [nameof(Rows)]);
            }
        }
    }
}

public sealed class AnimalImportRow
{
    /// <summary>Line number in the file, for error messages.</summary>
    public int? Line { get; set; }
    public string Name { get; set; } = "";
    public string? Species { get; set; }
    public string? Breed { get; set; }
    public string? Color { get; set; }
    /// <summary>
    /// As text (yyyy-mm-dd), so that an invalid date gives an error with the line number instead of
    /// an error while reading the JSON.
    /// </summary>
    public string? BirthDate { get; set; }
    /// <summary>Name of the location; an unknown location is created.</summary>
    public string? Location { get; set; }
    /// <summary>Empty: a new animal is active, an existing animal stays unchanged.</summary>
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
