using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Contracts;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;

namespace Wolsels.Api.Controllers;

[ApiController]
[Route("api/animals")]
public class AnimalsController(WolselsDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<AnimalDto>> List(CancellationToken ct)
    {
        var animals = await db.Animals.AsNoTracking().Include(a => a.Location)
            .OrderByDescending(a => a.Active).ThenBy(a => a.Name).ToListAsync(ct);
        var stats = await HarvestStatsAsync(null, ct);
        return animals.Select(a => ToDto(a, stats));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AnimalDto>> Get(int id, CancellationToken ct)
    {
        if (await db.Animals.AsNoTracking().Include(a => a.Location).FirstOrDefaultAsync(a => a.Id == id, ct)
            is not { } animal) return NotFound();
        return ToDto(animal, await HarvestStatsAsync(id, ct));
    }

    [HttpPost]
    public async Task<ActionResult<AnimalDto>> Create(AnimalRequest request, CancellationToken ct)
    {
        if (await ValidateLocationAsync(request, ct) is { } problem) return problem;
        var now = clock.GetUtcNow();
        var animal = new Animal { Name = "", Species = "", CreatedAt = now, UpdatedAt = now };
        request.ApplyTo(animal);
        db.Animals.Add(animal);
        await db.SaveChangesAsync(ct);
        await db.Entry(animal).Reference(a => a.Location).LoadAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = animal.Id }, AnimalDto.From(animal, 0, null));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AnimalDto>> Update(int id, AnimalRequest request, CancellationToken ct)
    {
        if (await db.Animals.FindAsync([id], ct) is not { } animal) return NotFound();
        if (await ValidateLocationAsync(request, ct) is { } problem) return problem;
        request.ApplyTo(animal);
        animal.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await db.Entry(animal).Reference(a => a.Location).LoadAsync(ct);
        return ToDto(animal, await HarvestStatsAsync(id, ct));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (await db.Animals.FindAsync([id], ct) is not { } animal) return NotFound();
        if (await db.Harvests.AnyAsync(h => h.AnimalId == id, ct))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Dit dier heeft oogsten. Zet het op inactief om de historie te bewaren.");
        db.Animals.Remove(animal);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Importeert dieren in één keer: bij een fout wordt niets opgeslagen. Dieren worden op naam (zonder
    /// onderscheid in hoofdletters) gekoppeld; onbekende locaties worden aangemaakt.
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<AnimalImportResult>> Import(AnimalImportRequest request, CancellationToken ct)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var existing = (await db.Animals.ToListAsync(ct)).ToLookup(a => a.Name.Trim(), comparer);
        var locations = (await db.Locations.ToListAsync(ct))
            .GroupBy(l => l.Name.Trim(), comparer).ToDictionary(g => g.Key, g => g.First(), comparer);
        var timesInFile = request.Rows.GroupBy(r => r.Name.Trim(), comparer).ToDictionary(g => g.Key, g => g.Count(), comparer);
        var newLocations = new List<string>();
        var now = clock.GetUtcNow();
        int created = 0, updated = 0, skipped = 0;

        for (var i = 0; i < request.Rows.Count; i++)
        {
            var row = request.Rows[i];
            var prefix = $"Regel {row.Line ?? i + 1}: ";
            var name = row.Name.Trim();
            var matches = existing[name].ToList();
            if (matches.Count > 0 && !request.UpdateExisting)
            {
                skipped++;
                continue;
            }
            if (matches.Count > 1)
            {
                ModelState.AddModelError(nameof(request.Rows), $"{prefix}er zijn meerdere dieren met de naam \"{name}\", dus het is niet duidelijk welke bijgewerkt moet worden.");
                continue;
            }
            if (matches.Count == 1 && timesInFile[name] > 1)
            {
                ModelState.AddModelError(nameof(request.Rows), $"{prefix}\"{name}\" staat meerdere keren in het bestand.");
                continue;
            }

            var animal = matches.SingleOrDefault();
            var isNew = animal is null;
            if (animal is null)
            {
                if ((row.Species.NullIfEmpty() ?? request.DefaultSpecies.NullIfEmpty()) is not { } newSpecies)
                {
                    ModelState.AddModelError(nameof(request.Rows), $"{prefix}diersoort ontbreekt.");
                    continue;
                }
                animal = new Animal { Name = name, Species = newSpecies, CreatedAt = now, Active = row.Active ?? true };
                db.Animals.Add(animal);
                created++;
            }
            else
            {
                updated++;
            }
            animal.UpdatedAt = now;

            // Een bestaand dier houdt zijn diersoort als die in het bestand leeg is; diersoort is verplicht.
            if ((isNew || request.Has("species")) && row.Species.NullIfEmpty() is { } species) animal.Species = species;
            if (isNew || request.Has("breed")) animal.Breed = row.Breed.NullIfEmpty();
            if (isNew || request.Has("color")) animal.Color = row.Color.NullIfEmpty();
            if (isNew || request.Has("birthDate")) animal.BirthDate = row.BirthDate;
            if (isNew || request.Has("notes")) animal.Notes = row.Notes.NullIfEmpty();
            if (request.Has("active") && row.Active is { } active) animal.Active = active;
            if (isNew || request.Has("location"))
            {
                if (row.Location.NullIfEmpty() is not { } locationName)
                {
                    animal.LocationId = null;
                    animal.Location = null;
                }
                else
                {
                    if (!locations.TryGetValue(locationName, out var location))
                    {
                        location = new Location { Name = locationName, CreatedAt = now, UpdatedAt = now };
                        db.Locations.Add(location);
                        locations[locationName] = location;
                        newLocations.Add(locationName);
                    }
                    animal.Location = location;
                }
            }
        }

        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        await db.SaveChangesAsync(ct);
        return new AnimalImportResult(created, updated, skipped, newLocations);
    }

    private async Task<ActionResult?> ValidateLocationAsync(AnimalRequest request, CancellationToken ct)
    {
        if (request.LocationId is { } locationId && !await db.Locations.AnyAsync(l => l.Id == locationId, ct))
        {
            ModelState.AddModelError(nameof(request.LocationId), "Deze locatie bestaat niet.");
            return ValidationProblem(ModelState);
        }
        return null;
    }

    private async Task<Dictionary<int, (decimal Total, DateOnly Last)>> HarvestStatsAsync(int? animalId, CancellationToken ct)
    {
        var harvests = await db.Harvests.AsNoTracking()
            .Where(h => h.AnimalId != null && (animalId == null || h.AnimalId == animalId))
            .Select(h => new { AnimalId = h.AnimalId!.Value, h.WeightGrams, h.Date })
            .ToListAsync(ct);
        return harvests.GroupBy(h => h.AnimalId)
            .ToDictionary(g => g.Key, g => (g.Sum(h => h.WeightGrams), g.Max(h => h.Date)));
    }

    private static AnimalDto ToDto(Animal animal, Dictionary<int, (decimal Total, DateOnly Last)> stats) =>
        stats.TryGetValue(animal.Id, out var s)
            ? AnimalDto.From(animal, s.Total, s.Last)
            : AnimalDto.From(animal, 0, null);
}
