using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Contracts;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;

namespace Wolsels.Api.Controllers;

[ApiController]
[Route("api/harvests")]
public class HarvestsController(WolselsDbContext db, TimeProvider clock) : ControllerBase
{
    private IQueryable<Harvest> HarvestsWithSource =>
        db.Harvests.Include(h => h.Animal).Include(h => h.Location);

    [HttpGet]
    public async Task<IEnumerable<HarvestDto>> List(
        [FromQuery] int? animalId, [FromQuery] int? locationId, [FromQuery] int? year, CancellationToken ct)
    {
        var query = HarvestsWithSource.AsNoTracking();
        if (animalId is not null) query = query.Where(h => h.AnimalId == animalId);
        if (locationId is not null) query = query.Where(h => h.LocationId == locationId);
        if (year is { } y)
        {
            var from = new DateOnly(y, 1, 1);
            var to = new DateOnly(y, 12, 31);
            query = query.Where(h => h.Date >= from && h.Date <= to);
        }
        return (await query.OrderByDescending(h => h.Date).ThenByDescending(h => h.Id).ToListAsync(ct))
            .Select(HarvestDto.From);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HarvestDto>> Get(int id, CancellationToken ct) =>
        await HarvestsWithSource.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct) is { } harvest
            ? HarvestDto.From(harvest)
            : NotFound();

    /// <summary>Totale productie ruwe wol, per maand en per dier/locatie.</summary>
    [HttpGet("summary")]
    public async Task<ProductionSummaryDto> Summary([FromQuery] int? year, CancellationToken ct)
    {
        var harvests = (await List(null, null, year, ct)).ToList();
        var perMonth = harvests
            .GroupBy(h => (h.Date.Year, h.Date.Month))
            .OrderBy(g => g.Key)
            .Select(g => new ProductionPeriodDto(g.Key.Year, g.Key.Month, g.Sum(h => h.WeightGrams), g.Count()))
            .ToList();
        var perSource = harvests
            .GroupBy(h => h.AnimalId is { } a ? ("animal", a, h.AnimalName ?? "") : ("location", h.LocationId!.Value, h.LocationName ?? ""))
            .Select(g => new ProductionSourceDto(g.Key.Item1, g.Key.Item2, g.Key.Item3, g.Sum(h => h.WeightGrams), g.Count()))
            .OrderByDescending(s => s.TotalGrams)
            .ToList();
        return new ProductionSummaryDto(harvests.Sum(h => h.WeightGrams), harvests.Count, perMonth, perSource);
    }

    [HttpPost]
    public async Task<ActionResult<HarvestDto>> Create(HarvestRequest request, CancellationToken ct)
    {
        if (await ValidateSourceAsync(request, ct) is { } problem) return problem;
        var now = clock.GetUtcNow();
        var harvest = new Harvest { CreatedAt = now, UpdatedAt = now };
        request.ApplyTo(harvest);

        if (request.AddToStock)
            harvest.Wool = await CreateStockAsync(request, now, ct);

        db.Harvests.Add(harvest);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = harvest.Id }, await LoadDtoAsync(harvest.Id, ct));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<HarvestDto>> Update(int id, HarvestRequest request, CancellationToken ct)
    {
        if (await db.Harvests.FindAsync([id], ct) is not { } harvest) return NotFound();
        if (await ValidateSourceAsync(request, ct) is { } problem) return problem;
        request.ApplyTo(harvest);
        harvest.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await LoadDtoAsync(id, ct);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (await db.Harvests.FindAsync([id], ct) is not { } harvest) return NotFound();
        db.Harvests.Remove(harvest);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ActionResult?> ValidateSourceAsync(HarvestRequest request, CancellationToken ct)
    {
        if (request.AnimalId is { } animalId && !await db.Animals.AnyAsync(a => a.Id == animalId, ct))
            ModelState.AddModelError(nameof(request.AnimalId), "Dit dier bestaat niet.");
        if (request.LocationId is { } locationId && !await db.Locations.AnyAsync(l => l.Id == locationId, ct))
            ModelState.AddModelError(nameof(request.LocationId), "Deze locatie bestaat niet.");
        return ModelState.IsValid ? null : ValidationProblem(ModelState);
    }

    private async Task<Wool> CreateStockAsync(HarvestRequest request, DateTimeOffset now, CancellationToken ct)
    {
        string name, woolType, color;
        if (request.AnimalId is { } animalId)
        {
            var animal = await db.Animals.FirstAsync(a => a.Id == animalId, ct);
            name = $"{animal.Name} {request.Date:yyyy-MM-dd}";
            woolType = animal.Breed ?? animal.Species;
            color = animal.Color ?? "Naturel";
        }
        else
        {
            var location = await db.Locations.FirstAsync(l => l.Id == request.LocationId, ct);
            name = $"{location.Name} {request.Date:yyyy-MM-dd}";
            woolType = "Eigen productie";
            color = "Naturel";
        }

        var wool = new Wool
        {
            Name = name,
            WoolType = woolType,
            Color = color,
            WeightGrams = request.WeightGrams,
            Origin = "Eigen productie",
            Notes = request.Grade is null ? "Ruwe wol" : $"Ruwe wol, {request.Grade}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Wools.Add(wool);
        return wool;
    }

    private async Task<HarvestDto> LoadDtoAsync(int id, CancellationToken ct) =>
        HarvestDto.From(await HarvestsWithSource.AsNoTracking().FirstAsync(h => h.Id == id, ct));
}
