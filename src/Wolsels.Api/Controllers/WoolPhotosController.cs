using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;
using Wolsels.Api.Services;
using Wolsels.Api.Localization;

namespace Wolsels.Api.Controllers;

/// <summary>
/// Photos of a stock lot. The browser resizes them before uploading; the server checks that they are
/// JPEGs of limited size.
/// </summary>
[ApiController]
[Route("api/wools/{woolId:int}/photos")]
public class WoolPhotosController(WolselsDbContext db, TimeProvider clock) : ControllerBase
{
    public const int MaxPhotosPerWool = 10;

    /// <summary>
    /// One change at a time to the number of photos (upload, merge), so that two concurrent requests
    /// cannot both take the last slot. There is a single backend; an in-process lock is enough.
    /// </summary>
    public static readonly SemaphoreSlim CountLock = new(1, 1);

    /// <summary>Longest side of a photo; the frontend resizes to 1600 px, with some margin.</summary>
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
        var data = await ReadJpegAsync(photo, nameof(photo), Texts.Get("Photos.Photo"), MaxPhotoBytes, MaxPhotoSide, ct);
        var thumb = await ReadJpegAsync(thumbnail, nameof(thumbnail), Texts.Get("Photos.Thumbnail"), MaxThumbnailBytes, MaxThumbnailSide, ct);
        if (data is null || thumb is null) return ValidationProblem(ModelState);

        var entity = new WoolPhoto { WoolId = woolId, Data = data, Thumbnail = thumb, CreatedAt = clock.GetUtcNow() };
        await CountLock.WaitAsync(ct);
        try
        {
            if (await db.WoolPhotos.CountAsync(p => p.WoolId == woolId, ct) >= MaxPhotosPerWool)
                return Problem(statusCode: StatusCodes.Status409Conflict, title: Texts.Get("Photos.TooMany", MaxPhotosPerWool));
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
    /// The photo, or with <c>?size=thumb</c> the thumbnail. The browser may keep it, but must ask again on every
    /// use (no-cache): that way a cached photo also passes the account and household check,
    /// e.g. after logging out or logging in as someone else. A photo never changes, so the ETag is fixed and the
    /// response is then usually a small 304.
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
            ModelState.AddModelError(key, Texts.Get("Photos.Missing", label));
            return null;
        }
        if (file.Length > maxBytes)
        {
            ModelState.AddModelError(key, Texts.Get("Photos.TooLarge", label, maxBytes / 1024));
            return null;
        }
        using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, ct);
        var bytes = stream.ToArray();
        if (!JpegInfo.TryReadSize(bytes, out var width, out var height))
        {
            ModelState.AddModelError(key, Texts.Get("Photos.NotJpeg", label));
            return null;
        }
        if (Math.Max(width, height) > maxSide)
        {
            ModelState.AddModelError(key, Texts.Get("Photos.TooManyPixels", label, width, height, maxSide));
            return null;
        }
        return bytes;
    }
}
