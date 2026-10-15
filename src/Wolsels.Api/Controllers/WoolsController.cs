using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Contracts;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;

namespace Wolsels.Api.Controllers;

[ApiController]
[Route("api/wools")]
public class WoolsController(WolselsDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<WoolDto>> List(CancellationToken ct) =>
        (await db.Wools.AsNoTracking().OrderBy(w => w.WoolType).ThenBy(w => w.Name).ToListAsync(ct))
            .Select(WoolDto.From);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WoolDto>> Get(int id, CancellationToken ct) =>
        await db.Wools.FindAsync([id], ct) is { } wool ? WoolDto.From(wool) : NotFound();

    [HttpPost]
    public async Task<ActionResult<WoolDto>> Create(WoolRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var wool = new Wool { Name = "", WoolType = "", Color = "", CreatedAt = now, UpdatedAt = now };
        request.ApplyTo(wool);
        db.Wools.Add(wool);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = wool.Id }, WoolDto.From(wool));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<WoolDto>> Update(int id, WoolRequest request, CancellationToken ct)
    {
        if (await db.Wools.FindAsync([id], ct) is not { } wool) return NotFound();
        request.ApplyTo(wool);
        wool.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return WoolDto.From(wool);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (await db.Wools.FindAsync([id], ct) is not { } wool) return NotFound();
        if (await db.BlendComponents.AnyAsync(c => c.WoolId == id, ct))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Deze wol wordt gebruikt in een mengsel en kan niet verwijderd worden.");
        db.Wools.Remove(wool);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
