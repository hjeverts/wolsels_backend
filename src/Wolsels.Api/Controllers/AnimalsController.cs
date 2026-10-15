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
