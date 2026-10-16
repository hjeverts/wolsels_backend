using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Contracts;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;
using Wolsels.Api.Services;
using Wolsels.Api.Localization;

namespace Wolsels.Api.Controllers;

[ApiController]
[Route("api/blends")]
public class BlendsController(WolselsDbContext db, BlendCalculator calculator, TimeProvider clock) : ControllerBase
{
    private IQueryable<Blend> BlendsWithComponents =>
        db.Blends.Include(b => b.Components).ThenInclude(c => c.Wool);

    [HttpGet]
    public async Task<IEnumerable<BlendDto>> List(CancellationToken ct) =>
        (await BlendsWithComponents.AsNoTracking().OrderByDescending(b => b.Id).ToListAsync(ct))
            .Select(BlendDto.From);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BlendDto>> Get(int id, CancellationToken ct) =>
        await BlendsWithComponents.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct) is { } blend
            ? BlendDto.From(blend)
            : NotFound();

    /// <summary>Converts a ratio to weights without saving anything.</summary>
    [HttpPost("calculate")]
    public ActionResult<BlendCalculationResult> Calculate(BlendCalculationRequest request)
    {
        try
        {
            return calculator.Calculate(new BlendCalculationInput(
                request.Ratios, request.Basis, request.TotalWeightGrams,
                request.BasisComponentIndex, request.BasisComponentWeightGrams));
        }
        catch (BlendCalculationException ex)
        {
            return CalculationProblem(ex);
        }
    }

    [HttpPost]
    public async Task<ActionResult<BlendDto>> Create(BlendRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var blend = new Blend { Name = "", CreatedAt = now };
        if (await ApplyAsync(blend, request, ct) is { } problem) return problem;
        blend.UpdatedAt = now;
        db.Blends.Add(blend);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = blend.Id }, await LoadDtoAsync(blend.Id, ct));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<BlendDto>> Update(int id, BlendRequest request, CancellationToken ct)
    {
        if (await db.Blends.Include(b => b.Components).FirstOrDefaultAsync(b => b.Id == id, ct) is not { } blend)
            return NotFound();
        if (await ApplyAsync(blend, request, ct) is { } problem) return problem;
        blend.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await LoadDtoAsync(id, ct);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (await db.Blends.Include(b => b.Components).FirstOrDefaultAsync(b => b.Id == id, ct) is not { } blend)
            return NotFound();
        // A deleted blend returns the deducted wool to stock.
        await RestoreStockAsync(blend, ct);
        db.Blends.Remove(blend);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ActionResult?> ApplyAsync(Blend blend, BlendRequest request, CancellationToken ct)
    {
        var woolIds = request.Components.Select(c => c.WoolId).Distinct().ToList();
        var wools = await db.Wools.Where(w => woolIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, ct);
        if (wools.Count != woolIds.Count)
        {
            ModelState.AddModelError(nameof(request.Components), Texts.Get("Blends.WoolMissing"));
            return ValidationProblem(ModelState);
        }

        BlendCalculationResult result;
        try
        {
            result = calculator.Calculate(new BlendCalculationInput(
                request.Components.Select(c => c.Ratio).ToList(), request.Basis, request.TotalWeightGrams,
                request.BasisComponentIndex, request.BasisComponentWeightGrams));
        }
        catch (BlendCalculationException ex)
        {
            return CalculationProblem(ex);
        }

        // First undo any earlier deduction, then deduct again if the blend has been spun.
        await RestoreStockAsync(blend, ct);
        if (request.Status == BlendStatus.Spun)
        {
            var shortages = request.Components
                .Select((c, i) => (c.WoolId, Weight: result.ComponentWeightsGrams[i]))
                .GroupBy(x => x.WoolId)
                .Select(g => (Wool: wools[g.Key], Needed: g.Sum(x => x.Weight)))
                .Where(x => x.Needed > x.Wool.WeightGrams)
                .Select(x => Texts.Get("Blends.Shortage", x.Wool.Name, x.Needed, x.Wool.WeightGrams))
                .ToList();
            if (shortages.Count > 0)
            {
                ModelState.AddModelError(nameof(request.Components),
                    Texts.Get("Blends.InsufficientStock", string.Join("; ", shortages)));
                return ValidationProblem(ModelState);
            }
            for (var i = 0; i < request.Components.Count; i++)
                wools[request.Components[i].WoolId].WeightGrams -= result.ComponentWeightsGrams[i];
        }
        blend.StockDeducted = request.Status == BlendStatus.Spun;

        blend.Name = request.Name.Trim();
        blend.Status = request.Status;
        blend.Notes = request.Notes.NullIfEmpty();
        blend.Basis = request.Basis;
        blend.BasisComponentIndex = request.Basis == CalculationBasis.ComponentWeight ? request.BasisComponentIndex : null;
        blend.InputWeightGrams = result.TotalWeightGrams;

        blend.SpunOn = request.SpunOn;
        blend.OutputWeightGrams = request.OutputWeightGrams;
        blend.OutputLengthMeters = request.OutputLengthMeters;
        blend.YarnWeight = request.YarnWeight.NullIfEmpty();
        blend.WrapsPerInch = request.WrapsPerInch;
        blend.PlyCount = request.PlyCount;
        blend.PlyMethod = request.PlyMethod.NullIfEmpty();
        blend.SpinDirection = request.SpinDirection.NullIfEmpty();

        blend.IsDyed = request.IsDyed;
        blend.DyeColor = request.IsDyed ? request.DyeColor.NullIfEmpty() : null;
        blend.DyeColorHex = request.IsDyed && !string.IsNullOrWhiteSpace(request.DyeColorHex)
            ? request.DyeColorHex.ToLowerInvariant()
            : null;
        blend.DyeProduct = request.IsDyed ? request.DyeProduct.NullIfEmpty() : null;
        blend.DyeMethod = request.IsDyed ? request.DyeMethod.NullIfEmpty() : null;

        db.BlendComponents.RemoveRange(blend.Components);
        blend.Components = request.Components
            .Select((c, i) => new BlendComponent
            {
                Position = i,
                WoolId = c.WoolId,
                Ratio = c.Ratio,
                WeightGrams = result.ComponentWeightsGrams[i],
            })
            .ToList();
        return null;
    }

    /// <summary>Returns the previously deducted weights to stock (not saved yet).</summary>
    private async Task RestoreStockAsync(Blend blend, CancellationToken ct)
    {
        if (!blend.StockDeducted) return;
        var woolIds = blend.Components.Select(c => c.WoolId).Distinct().ToList();
        var wools = await db.Wools.Where(w => woolIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, ct);
        foreach (var component in blend.Components)
        {
            if (wools.TryGetValue(component.WoolId, out var wool)) wool.WeightGrams += component.WeightGrams;
        }
        blend.StockDeducted = false;
    }

    private async Task<BlendDto> LoadDtoAsync(int id, CancellationToken ct) =>
        BlendDto.From(await BlendsWithComponents.AsNoTracking().FirstAsync(b => b.Id == id, ct));

    private ActionResult CalculationProblem(BlendCalculationException ex)
    {
        ModelState.AddModelError(ex.Field, ex.Message);
        return ValidationProblem(ModelState);
    }
}
