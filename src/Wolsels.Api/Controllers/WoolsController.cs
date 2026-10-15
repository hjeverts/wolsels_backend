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

    /// <summary>
    /// Voegt deze partij samen met een andere: het gewicht gaat over, en oogsten en mengsels die naar deze partij
    /// verwijzen, verwijzen daarna naar de andere. Deze partij verdwijnt.
    /// </summary>
    [HttpPost("{id:int}/merge")]
    public async Task<ActionResult<WoolDto>> Merge(int id, MergeWoolRequest request, CancellationToken ct)
    {
        if (id == request.TargetWoolId)
        {
            ModelState.AddModelError(nameof(request.TargetWoolId), "Kies een andere partij om mee samen te voegen.");
            return ValidationProblem(ModelState);
        }
        if (await db.Wools.FindAsync([id], ct) is not { } source) return NotFound();
        if (await db.Wools.FirstOrDefaultAsync(w => w.Id == request.TargetWoolId, ct) is not { } target)
        {
            ModelState.AddModelError(nameof(request.TargetWoolId), "Deze voorraadpartij bestaat niet.");
            return ValidationProblem(ModelState);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        target.WeightGrams += source.WeightGrams;
        target.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await db.BlendComponents.Where(c => c.WoolId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.WoolId, target.Id), ct);
        await db.Harvests.Where(h => h.WoolId == id).ExecuteUpdateAsync(s => s.SetProperty(h => h.WoolId, (int?)target.Id), ct);
        await db.Wools.Where(w => w.Id == id).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return WoolDto.From(target);
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
