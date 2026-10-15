using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Contracts;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;

namespace Wolsels.Api.Controllers;

[ApiController]
[Route("api/locations")]
public class LocationsController(WolselsDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<LocationDto>> List(CancellationToken ct)
    {
        var counts = await db.Animals.Where(a => a.LocationId != null && a.Active)
            .GroupBy(a => a.LocationId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return (await db.Locations.AsNoTracking().OrderBy(l => l.Name).ToListAsync(ct))
            .Select(l => LocationDto.From(l, counts.GetValueOrDefault(l.Id)));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LocationDto>> Get(int id, CancellationToken ct)
    {
        if (await db.Locations.FindAsync([id], ct) is not { } location) return NotFound();
        return LocationDto.From(location, await db.Animals.CountAsync(a => a.LocationId == id && a.Active, ct));
    }

    [HttpPost]
    public async Task<ActionResult<LocationDto>> Create(LocationRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var location = new Location { Name = "", CreatedAt = now, UpdatedAt = now };
        request.ApplyTo(location);
        db.Locations.Add(location);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = location.Id }, LocationDto.From(location, 0));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<LocationDto>> Update(int id, LocationRequest request, CancellationToken ct)
    {
        if (await db.Locations.FindAsync([id], ct) is not { } location) return NotFound();
        request.ApplyTo(location);
        location.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return LocationDto.From(location, await db.Animals.CountAsync(a => a.LocationId == id && a.Active, ct));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (await db.Locations.FindAsync([id], ct) is not { } location) return NotFound();
        if (await db.Harvests.AnyAsync(h => h.LocationId == id, ct))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Op deze locatie zijn oogsten geboekt; verwijder die eerst.");
        db.Locations.Remove(location);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
