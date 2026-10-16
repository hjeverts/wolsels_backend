using System.Net;
using System.Net.Http.Json;
using Wolsels.Api.Contracts;

namespace Wolsels.Api.Tests;

public class StockTests(WolselsFactory factory) : IClassFixture<WolselsFactory>
{
    private readonly HttpClient _client = factory.CreateUserClientAsync().GetAwaiter().GetResult();

    private async Task<int> CreateWoolAsync(decimal weight) =>
        (await (await _client.PostAsJsonAsync("/api/wools",
                new { name = $"Wol {Guid.NewGuid():N}", woolType = "Merino", color = "Wit", weightGrams = weight }, TestJson.Options))
            .Content.ReadFromJsonAsync<WoolDto>(TestJson.Options))!.Id;

    private async Task<decimal> StockAsync(int woolId) =>
        (await _client.GetFromJsonAsync<WoolDto>($"/api/wools/{woolId}", TestJson.Options))!.WeightGrams;

    private static object Blend(string status, int woolA, int woolB, decimal total) => new
    {
        name = "Afboeken",
        status,
        components = new[] { new { woolId = woolA, ratio = 3 }, new { woolId = woolB, ratio = 1 } },
        basis = "TotalWeight",
        totalWeightGrams = total,
    };

    [Fact]
    public async Task SpinningDeductsStock_AndUndoingRestoresIt()
    {
        var a = await CreateWoolAsync(500);
        var b = await CreateWoolAsync(200);

        var planned = await _client.PostAsJsonAsync("/api/blends", Blend("Planned", a, b, 200), TestJson.Options);
        var blend = (await planned.Content.ReadFromJsonAsync<BlendDto>(TestJson.Options))!;
        Assert.False(blend.StockDeducted);
        Assert.Equal(500m, await StockAsync(a));

        var spun = await _client.PutAsJsonAsync($"/api/blends/{blend.Id}", Blend("Spun", a, b, 200), TestJson.Options);
        Assert.True((await spun.Content.ReadFromJsonAsync<BlendDto>(TestJson.Options))!.StockDeducted);
        Assert.Equal(350m, await StockAsync(a));
        Assert.Equal(150m, await StockAsync(b));

        // Changing the weight while spun: the difference is settled.
        await _client.PutAsJsonAsync($"/api/blends/{blend.Id}", Blend("Spun", a, b, 100), TestJson.Options);
        Assert.Equal(425m, await StockAsync(a));
        Assert.Equal(175m, await StockAsync(b));

        await _client.PutAsJsonAsync($"/api/blends/{blend.Id}", Blend("Planned", a, b, 100), TestJson.Options);
        Assert.Equal(500m, await StockAsync(a));
        Assert.Equal(200m, await StockAsync(b));
    }

    [Fact]
    public async Task DeletingSpunBlend_RestoresStock()
    {
        var a = await CreateWoolAsync(500);
        var b = await CreateWoolAsync(200);
        var blend = (await (await _client.PostAsJsonAsync("/api/blends", Blend("Spun", a, b, 200), TestJson.Options))
            .Content.ReadFromJsonAsync<BlendDto>(TestJson.Options))!;
        Assert.Equal(350m, await StockAsync(a));

        await _client.DeleteAsync($"/api/blends/{blend.Id}");

        Assert.Equal(500m, await StockAsync(a));
        Assert.Equal(200m, await StockAsync(b));
    }

    [Fact]
    public async Task InsufficientStock_IsRejectedWithoutChanges()
    {
        var a = await CreateWoolAsync(100);
        var b = await CreateWoolAsync(100);

        var response = await _client.PostAsJsonAsync("/api/blends", Blend("Spun", a, b, 200), TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Onvoldoende voorraad", await response.Content.ReadAsStringAsync());
        Assert.Equal(100m, await StockAsync(a));
    }
}
