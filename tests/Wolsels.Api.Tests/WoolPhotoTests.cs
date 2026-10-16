using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolsels.Api.Contracts;
using Wolsels.Api.Controllers;
using Wolsels.Api.Data;
using Wolsels.Api.Households;
using Wolsels.Api.Services;

namespace Wolsels.Api.Tests;

public class JpegInfoTests
{
    /// <summary>A real (progressive) 37×21 pixel JPEG with an EXIF block, made with Pillow.</summary>
    private static readonly byte[] RealJpeg = Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/4QAWRXhpZgAATU0AKgAAAAgAAAAAAAD/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYnKSopGR8tMC0oMCUoKSj/2wBDAQcHBwoIChMKChMoGhYaKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCj/wgARCAAVACUDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAP/xAAVAQEBAAAAAAAAAAAAAAAAAAAABf/aAAwDAQACEAMQAAABoIFsAAAAD//EABQQAQAAAAAAAAAAAAAAAAAAADD/2gAIAQEAAQUCf//EABQRAQAAAAAAAAAAAAAAAAAAACD/2gAIAQMBAT8BX//EABQRAQAAAAAAAAAAAAAAAAAAACD/2gAIAQIBAT8BX//EABQQAQAAAAAAAAAAAAAAAAAAADD/2gAIAQEABj8Cf//EABQQAQAAAAAAAAAAAAAAAAAAADD/2gAIAQEAAT8hf//aAAwDAQACAAMAAAAQ++++++//xAAUEQEAAAAAAAAAAAAAAAAAAAAg/9oACAEDAQE/EF//xAAUEQEAAAAAAAAAAAAAAAAAAAAg/9oACAECAQE/EF//xAAUEAEAAAAAAAAAAAAAAAAAAAAw/9oACAEBAAE/EH//2Q==");

    [Fact]
    public void ReadsSizeOfRealJpeg() 
    {
        Assert.True(JpegInfo.TryReadSize(RealJpeg, out var width, out var height));
        Assert.Equal((37, 21), (width, height));
    }

    [Fact]
    public void ReadsSizeOfMinimalJpeg()
    {
        Assert.True(JpegInfo.TryReadSize(WoolPhotoTests.Jpeg(1600, 1200), out var width, out var height));
        Assert.Equal((1600, 1200), (width, height));
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })] // PNG
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0xFF })] // truncated
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 })] // no SOF
    [InlineData(new byte[] { })]
    public void RejectsOtherData(byte[] data) => Assert.False(JpegInfo.TryReadSize(data, out _, out _));
}

public class WoolPhotoTests(WolselsFactory factory) : IClassFixture<WolselsFactory>
{
    /// <summary>Smallest valid JPEG header with the given dimensions (SOI, SOF0, EOI).</summary>
    public static byte[] Jpeg(int width, int height, int padding = 0) =>
    [
        0xFF, 0xD8,
        0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
        0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        .. new byte[padding],
        0xFF, 0xD9,
    ];

    private static async Task<int> WoolAsync(HttpClient client, string name = "Merino wit") =>
        (await (await client.PostAsJsonAsync("/api/wools", new { name, woolType = "Merino", color = "Wit", weightGrams = 100 }, TestJson.Options))
            .Content.ReadFromJsonAsync<WoolDto>(TestJson.Options))!.Id;

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, int woolId, byte[]? photo, byte[]? thumbnail)
    {
        var form = new MultipartFormDataContent();
        if (photo is not null) form.Add(File(photo), "photo", "foto.jpg");
        if (thumbnail is not null) form.Add(File(thumbnail), "thumbnail", "mini.jpg");
        return client.PostAsync($"/api/wools/{woolId}/photos", form);

        static ByteArrayContent File(byte[] bytes)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return content;
        }
    }

    private static async Task<int> UploadOkAsync(HttpClient client, int woolId)
    {
        var response = await UploadAsync(client, woolId, Jpeg(1600, 1200), Jpeg(400, 300));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<int>();
    }

    private int PhotoCountInDatabase()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<WolselsDbContext>().WoolPhotos.IgnoreQueryFilters().Count();
    }

    [Fact]
    public async Task Photo_CanBeUploaded_Listed_AndDownloaded()
    {
        var client = await factory.CreateUserClientAsync();
        var wool = await WoolAsync(client);
        var photo = Jpeg(1600, 1200, padding: 10);
        var response = await UploadAsync(client, wool, photo, Jpeg(400, 300));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = await response.Content.ReadFromJsonAsync<int>();

        Assert.Equal([id], (await client.GetFromJsonAsync<WoolDto>($"/api/wools/{wool}", TestJson.Options))!.PhotoIds);
        var listed = await client.GetFromJsonAsync<List<WoolDto>>("/api/wools", TestJson.Options);
        Assert.Equal([id], listed!.Single(w => w.Id == wool).PhotoIds);

        var full = await client.GetAsync($"/api/wools/{wool}/photos/{id}");
        Assert.Equal("image/jpeg", full.Content.Headers.ContentType?.MediaType);
        Assert.Equal(photo, await full.Content.ReadAsByteArrayAsync());
        Assert.True(full.Headers.CacheControl!.NoCache);
        Assert.True(full.Headers.CacheControl.Private);
        var thumb = await client.GetByteArrayAsync($"/api/wools/{wool}/photos/{id}?size=thumb");
        Assert.Equal(Jpeg(400, 300), thumb);
    }

    [Fact]
    public async Task CachedPhoto_IsRevalidated_AndStillChecksAccess()
    {
        var client = await factory.CreateUserClientAsync();
        var wool = await WoolAsync(client);
        var photo = await UploadOkAsync(client, wool);
        var url = $"/api/wools/{wool}/photos/{photo}";
        var etag = (await client.GetAsync(url)).Headers.ETag!;
        Assert.NotEqual(etag, (await client.GetAsync(url + "?size=thumb")).Headers.ETag);

        HttpRequestMessage Conditional(HttpClient c) =>
            new(HttpMethod.Get, url) { Headers = { IfNoneMatch = { etag } } };
        var revalidated = await client.SendAsync(Conditional(client));
        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);

        // Someone else in the same browser (same cache, same ETag) doesn't get the photo.
        var other = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(Conditional(other))).StatusCode);
        await client.DeleteAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Conditional(client))).StatusCode);
    }

    [Fact]
    public async Task InvalidUploads_AreRejected()
    {
        var client = await factory.CreateUserClientAsync();
        var wool = await WoolAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(client, wool, [0x89, 0x50, 0x4E, 0x47], Jpeg(400, 300))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(client, wool, Jpeg(4000, 3000), Jpeg(400, 300))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(client, wool, Jpeg(1600, 1200), Jpeg(1600, 1200))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(client, wool, Jpeg(1600, 1200), null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await UploadAsync(client, wool, Jpeg(1600, 1200, padding: 2 * 1024 * 1024), Jpeg(400, 300))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(client, 999_999, Jpeg(1600, 1200), Jpeg(400, 300))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<WoolDto>($"/api/wools/{wool}", TestJson.Options))!.PhotoIds);
    }

    [Fact]
    public async Task AtMostTenPhotos_PerWool()
    {
        var client = await factory.CreateUserClientAsync();
        var wool = await WoolAsync(client);
        for (var i = 0; i < WoolPhotosController.MaxPhotosPerWool; i++) await UploadOkAsync(client, wool);
        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(client, wool, Jpeg(800, 600), Jpeg(200, 150))).StatusCode);
    }

    [Fact]
    public async Task ConcurrentUploads_CannotExceedTheLimit()
    {
        var client = await factory.CreateUserClientAsync();
        var wool = await WoolAsync(client);
        for (var i = 0; i < WoolPhotosController.MaxPhotosPerWool - 1; i++) await UploadOkAsync(client, wool);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => UploadAsync(client, wool, Jpeg(800, 600), Jpeg(200, 150))));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(WoolPhotosController.MaxPhotosPerWool,
            (await client.GetFromJsonAsync<WoolDto>($"/api/wools/{wool}", TestJson.Options))!.PhotoIds.Count);
    }

    [Fact]
    public async Task DeletingPhoto_RemovesIt()
    {
        var client = await factory.CreateUserClientAsync();
        var wool = await WoolAsync(client);
        var keep = await UploadOkAsync(client, wool);
        var remove = await UploadOkAsync(client, wool);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/wools/{wool}/photos/{remove}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/wools/{wool}/photos/{remove}")).StatusCode);
        Assert.Equal([keep], (await client.GetFromJsonAsync<WoolDto>($"/api/wools/{wool}", TestJson.Options))!.PhotoIds);
    }

    [Fact]
    public async Task DeletingWool_DeletesItsPhotosFromTheDatabase()
    {
        var client = await factory.CreateUserClientAsync();
        var wool = await WoolAsync(client);
        var photo = await UploadOkAsync(client, wool);
        await UploadOkAsync(client, wool);
        var before = PhotoCountInDatabase();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/wools/{wool}")).StatusCode);
        Assert.Equal(before - 2, PhotoCountInDatabase());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/wools/{wool}/photos/{photo}")).StatusCode);
    }

    [Fact]
    public async Task DeletingHousehold_DeletesItsPhotos()
    {
        var client = await factory.CreateUserClientAsync();
        await UploadOkAsync(client, await WoolAsync(client));
        var household = (await client.GetFromJsonAsync<List<HouseholdSummaryDto>>("/api/households", TestJson.Options))!.Single().Id;
        var before = PhotoCountInDatabase();

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HouseholdService>().DeleteHouseholdAsync(household, CancellationToken.None);
        Assert.Equal(before - 1, PhotoCountInDatabase());
    }

    [Fact]
    public async Task MergingWool_MovesPhotosToTarget()
    {
        var client = await factory.CreateUserClientAsync();
        var source = await WoolAsync(client, "Bron");
        var target = await WoolAsync(client, "Doel");
        var photo = await UploadOkAsync(client, source);

        var merged = await (await client.PostAsJsonAsync($"/api/wools/{source}/merge", new { targetWoolId = target }, TestJson.Options))
            .Content.ReadFromJsonAsync<WoolDto>(TestJson.Options);
        Assert.Equal([photo], merged!.PhotoIds);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/wools/{target}/photos/{photo}")).StatusCode);
    }

    [Fact]
    public async Task MergingOverThePhotoLimit_IsRejected_AndChangesNothing()
    {
        var client = await factory.CreateUserClientAsync();
        var source = await WoolAsync(client, "Zes");
        var target = await WoolAsync(client, "Ook zes");
        for (var i = 0; i < 6; i++)
        {
            await UploadOkAsync(client, source);
            await UploadOkAsync(client, target);
        }

        var response = await client.PostAsJsonAsync($"/api/wools/{source}/merge", new { targetWoolId = target }, TestJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var wools = await client.GetFromJsonAsync<List<WoolDto>>("/api/wools", TestJson.Options);
        Assert.Equal(6, wools!.Single(w => w.Id == source).PhotoIds.Count);
        Assert.Equal(6, wools.Single(w => w.Id == target).PhotoIds.Count);
        Assert.Equal(100m, wools.Single(w => w.Id == target).WeightGrams);
    }

    [Fact]
    public async Task Photos_AreOnlyVisibleInOwnHousehold()
    {
        var owner = await factory.CreateUserClientAsync();
        var wool = await WoolAsync(owner);
        var photo = await UploadOkAsync(owner, wool);
        var ownHousehold = (await owner.GetFromJsonAsync<List<HouseholdSummaryDto>>("/api/households", TestJson.Options))!.Single().Id;

        var other = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/wools/{wool}/photos/{photo}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/wools/{wool}/photos/{photo}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(other, wool, Jpeg(800, 600), Jpeg(200, 150))).StatusCode);
        // Not even by putting the owner's household in the URL.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.GetAsync($"/api/wools/{wool}/photos/{photo}?{HouseholdMiddleware.QueryName}={ownHousehold}")).StatusCode);
    }

    [Fact]
    public async Task HouseholdInQuery_SelectsHousehold_ForImages()
    {
        var client = await factory.CreateUserClientAsync();
        var first = (await client.GetFromJsonAsync<List<HouseholdSummaryDto>>("/api/households", TestJson.Options))!.Single().Id;
        var wool = await WoolAsync(client);
        var photo = await UploadOkAsync(client, wool);
        var second = (await (await client.PostAsJsonAsync("/api/households", new { name = "Tweede" }, TestJson.Options))
            .Content.ReadFromJsonAsync<HouseholdSummaryDto>(TestJson.Options))!.Id;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/wools/{wool}/photos/{photo}?household={first}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/wools/{wool}/photos/{photo}?household={second}")).StatusCode);
        // An invalid value is rejected, just like with the header.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/wools/{wool}/photos/{photo}?household=abc")).StatusCode);
    }
}
