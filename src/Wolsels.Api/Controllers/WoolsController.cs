using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Contracts;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;
using Wolsels.Api.Localization;

namespace Wolsels.Api.Controllers;

[ApiController]
[Route("api/wools")]
public class WoolsController(WolselsDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<WoolDto>> List(CancellationToken ct)
    {
        var wools = await db.Wools.AsNoTracking().OrderBy(w => w.WoolType).ThenBy(w => w.Name).ToListAsync(ct);
        var photos = (await db.WoolPhotos.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.WoolId, p.Id }).ToListAsync(ct))
            .ToLookup(p => p.WoolId, p => p.Id);
        return wools.Select(w => WoolDto.From(w, [.. photos[w.Id]]));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WoolDto>> Get(int id, CancellationToken ct) =>
        await db.Wools.FindAsync([id], ct) is { } wool ? await ToDtoAsync(wool, ct) : NotFound();

    [HttpPost]
    public async Task<ActionResult<WoolDto>> Create(WoolRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var wool = new Wool { Name = "", WoolType = "", Color = "", CreatedAt = now, UpdatedAt = now };
        request.ApplyTo(wool);
        db.Wools.Add(wool);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = wool.Id }, WoolDto.From(wool, []));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<WoolDto>> Update(int id, WoolRequest request, CancellationToken ct)
    {
        if (await db.Wools.FindAsync([id], ct) is not { } wool) return NotFound();
        request.ApplyTo(wool);
        wool.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(wool, ct);
    }

    /// <summary>
    /// Merges this lot into another: the weight moves over, and harvests and blends that refer to this lot
    /// refer to the other one afterwards. This lot disappears.
    /// </summary>
    [HttpPost("{id:int}/merge")]
    public async Task<ActionResult<WoolDto>> Merge(int id, MergeWoolRequest request, CancellationToken ct)
    {
        if (id == request.TargetWoolId)
        {
            ModelState.AddModelError(nameof(request.TargetWoolId), Texts.Get("Wools.MergeChooseOther"));
            return ValidationProblem(ModelState);
        }
        if (await db.Wools.FindAsync([id], ct) is not { } source) return NotFound();
        if (await db.Wools.FirstOrDefaultAsync(w => w.Id == request.TargetWoolId, ct) is not { } target)
        {
            ModelState.AddModelError(nameof(request.TargetWoolId), Texts.Get("Wools.NotFound"));
            return ValidationProblem(ModelState);
        }

        // Together no more than the maximum number of photos; the lock keeps an upload from slipping in between.
        await WoolPhotosController.CountLock.WaitAsync(ct);
        try
        {
            if (await db.WoolPhotos.CountAsync(p => p.WoolId == id || p.WoolId == target.Id, ct) > WoolPhotosController.MaxPhotosPerWool)
                return Problem(statusCode: StatusCodes.Status409Conflict,
                    title: Texts.Get("Wools.MergeTooManyPhotos", WoolPhotosController.MaxPhotosPerWool));
            await MergeAsync(id, source, target, ct);
        }
        finally
        {
            WoolPhotosController.CountLock.Release();
        }
        return await ToDtoAsync(target, ct);
    }

    private async Task MergeAsync(int id, Wool source, Wool target, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        target.WeightGrams += source.WeightGrams;
        target.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await db.BlendComponents.Where(c => c.WoolId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.WoolId, target.Id), ct);
        await db.Harvests.Where(h => h.WoolId == id).ExecuteUpdateAsync(s => s.SetProperty(h => h.WoolId, (int?)target.Id), ct);
        await db.WoolPhotos.Where(p => p.WoolId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.WoolId, target.Id), ct);
        await db.Wools.Where(w => w.Id == id).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<WoolDto> ToDtoAsync(Wool wool, CancellationToken ct) =>
        WoolDto.From(wool, await db.WoolPhotos.Where(p => p.WoolId == wool.Id).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(ct));

    /// <summary>Deletes the lot; the database deletes its photos along with it.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (await db.Wools.FindAsync([id], ct) is not { } wool) return NotFound();
        if (await db.BlendComponents.AnyAsync(c => c.WoolId == id, ct))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: Texts.Get("Wools.InUse"));
        db.Wools.Remove(wool);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
