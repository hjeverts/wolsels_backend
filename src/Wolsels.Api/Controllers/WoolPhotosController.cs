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
        if (await db.WoolPhotos.CountAsync(p => p.WoolId == woolId, ct) >= MaxPhotosPerWool)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: $"Een partij kan maximaal {MaxPhotosPerWool} foto's hebben.");

        var data = await ReadJpegAsync(photo, nameof(photo), "De foto", MaxPhotoBytes, MaxPhotoSide, ct);
        var thumb = await ReadJpegAsync(thumbnail, nameof(thumbnail), "De miniatuur", MaxThumbnailBytes, MaxThumbnailSide, ct);
        if (data is null || thumb is null) return ValidationProblem(ModelState);

        var entity = new WoolPhoto { WoolId = woolId, Data = data, Thumbnail = thumb, CreatedAt = clock.GetUtcNow() };
        db.WoolPhotos.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { woolId, photoId = entity.Id }, entity.Id);
    }

    /// <summary>
    /// De foto, of met <c>?size=thumb</c> de miniatuur. Een foto verandert nooit (alleen toevoegen of
    /// verwijderen), dus de browser mag hem lang bewaren.
    /// </summary>
    [HttpGet("{photoId:int}")]
    public async Task<IActionResult> Get(int woolId, int photoId, string? size, CancellationToken ct)
    {
        var photos = db.WoolPhotos.AsNoTracking().Where(p => p.Id == photoId && p.WoolId == woolId);
        var bytes = size == "thumb"
            ? await photos.Select(p => p.Thumbnail).FirstOrDefaultAsync(ct)
            : await photos.Select(p => p.Data).FirstOrDefaultAsync(ct);
        if (bytes is null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return File(bytes, "image/jpeg");
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
