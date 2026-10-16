using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;
using Wolsels.Api.Services;

namespace Wolsels.Api.Controllers;

/// <summary>
/// Foto's van een voorraadpartij. De browser verkleint ze vóór het uploaden; de server controleert dat het
/// JPEG's van een beperkt formaat zijn.
/// </summary>
[ApiController]
[Route("api/wools/{woolId:int}/photos")]
public class WoolPhotosController(WolselsDbContext db, TimeProvider clock) : ControllerBase
{
    public const int MaxPhotosPerWool = 10;

    /// <summary>
    /// Eén wijziging tegelijk aan het aantal foto's (uploaden, samenvoegen), zodat twee gelijktijdige verzoeken
    /// niet allebei de laatste plek kunnen innemen. Er draait één backend; een slot in het proces volstaat.
    /// </summary>
    public static readonly SemaphoreSlim CountLock = new(1, 1);

    public static readonly string TooManyPhotos = $"Een partij kan maximaal {MaxPhotosPerWool} foto's hebben.";
    /// <summary>Langste zijde van een foto; de frontend verkleint tot 1600 px, met wat ruimte.</summary>
    public const int MaxPhotoSide = 2048;
    public const int MaxThumbnailSide = 512;
    public const int MaxPhotoBytes = 2 * 1024 * 1024;
    public const int MaxThumbnailBytes = 256 * 1024;
    private const int MaxRequestBytes = MaxPhotoBytes + MaxThumbnailBytes + 64 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<int>> Upload(int woolId, IFormFile? photo, IFormFile? thumbnail, CancellationToken ct)
    {
        if (await db.Wools.FindAsync([woolId], ct) is null) return NotFound();
        var data = await ReadJpegAsync(photo, nameof(photo), "De foto", MaxPhotoBytes, MaxPhotoSide, ct);
        var thumb = await ReadJpegAsync(thumbnail, nameof(thumbnail), "De miniatuur", MaxThumbnailBytes, MaxThumbnailSide, ct);
        if (data is null || thumb is null) return ValidationProblem(ModelState);

        var entity = new WoolPhoto { WoolId = woolId, Data = data, Thumbnail = thumb, CreatedAt = clock.GetUtcNow() };
        await CountLock.WaitAsync(ct);
        try
        {
            if (await db.WoolPhotos.CountAsync(p => p.WoolId == woolId, ct) >= MaxPhotosPerWool)
                return Problem(statusCode: StatusCodes.Status409Conflict, title: TooManyPhotos);
            db.WoolPhotos.Add(entity);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            CountLock.Release();
        }
        return CreatedAtAction(nameof(Get), new { woolId, photoId = entity.Id }, entity.Id);
    }

    /// <summary>
    /// De foto, of met <c>?size=thumb</c> de miniatuur. De browser mag hem bewaren, maar moet bij elk gebruik
    /// opnieuw vragen (no-cache): zo gaat ook een foto uit de cache langs de controle op account en huishouden,
    /// bv. na uitloggen of inloggen als iemand anders. Een foto verandert nooit, dus de ETag is vast en het
    /// antwoord is dan meestal een kleine 304.
    /// </summary>
    [HttpGet("{photoId:int}")]
    public async Task<IActionResult> Get(int woolId, int photoId, string? size, CancellationToken ct)
    {
        var thumb = size == "thumb";
        var photos = db.WoolPhotos.AsNoTracking().Where(p => p.Id == photoId && p.WoolId == woolId);
        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{photoId}{(thumb ? "t" : "")}\"");
        Response.Headers.CacheControl = "private, no-cache";

        if (Request.GetTypedHeaders().IfNoneMatch.Any(t => t.Compare(etag, useStrongComparison: false)))
        {
            if (!await photos.AnyAsync(ct)) return NotFound();
            Response.GetTypedHeaders().ETag = etag;
            return StatusCode(StatusCodes.Status304NotModified);
        }

        var bytes = thumb
            ? await photos.Select(p => p.Thumbnail).FirstOrDefaultAsync(ct)
            : await photos.Select(p => p.Data).FirstOrDefaultAsync(ct);
        if (bytes is null) return NotFound();
        return File(bytes, "image/jpeg", lastModified: null, entityTag: etag);
    }

    [HttpDelete("{photoId:int}")]
    public async Task<IActionResult> Delete(int woolId, int photoId, CancellationToken ct)
    {
        var deleted = await db.WoolPhotos.Where(p => p.Id == photoId && p.WoolId == woolId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound() : NoContent();
    }

    private async Task<byte[]?> ReadJpegAsync(IFormFile? file, string key, string label, int maxBytes, int maxSide, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(key, $"{label} ontbreekt.");
            return null;
        }
        if (file.Length > maxBytes)
        {
            ModelState.AddModelError(key, $"{label} is groter dan {maxBytes / 1024} kB.");
            return null;
        }
        using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, ct);
        var bytes = stream.ToArray();
        if (!JpegInfo.TryReadSize(bytes, out var width, out var height))
        {
            ModelState.AddModelError(key, $"{label} is geen JPEG-afbeelding.");
            return null;
        }
        if (Math.Max(width, height) > maxSide)
        {
            ModelState.AddModelError(key, $"{label} is {width}×{height} pixels; maximaal {maxSide} pixels aan de lange kant.");
            return null;
        }
        return bytes;
    }
}
