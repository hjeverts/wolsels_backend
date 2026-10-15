using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Contracts;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;
using Wolsels.Api.Services;

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

    /// <summary>Rekent een verhouding om naar gewichten zonder iets op te slaan.</summary>
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
        if (await db.Blends.FindAsync([id], ct) is not { } blend) return NotFound();
        db.Blends.Remove(blend);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ActionResult?> ApplyAsync(Blend blend, BlendRequest request, CancellationToken ct)
    {
        var woolIds = request.Components.Select(c => c.WoolId).Distinct().ToList();
        var existing = await db.Wools.Where(w => woolIds.Contains(w.Id)).Select(w => w.Id).ToListAsync(ct);
        if (existing.Count != woolIds.Count)
        {
            ModelState.AddModelError(nameof(request.Components), "Een of meer gekozen soorten wol bestaan niet meer.");
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

    private async Task<BlendDto> LoadDtoAsync(int id, CancellationToken ct) =>
        BlendDto.From(await BlendsWithComponents.AsNoTracking().FirstAsync(b => b.Id == id, ct));

    private ActionResult CalculationProblem(BlendCalculationException ex)
    {
        ModelState.AddModelError(ex.Field, ex.Message);
        return ValidationProblem(ModelState);
    }
}
