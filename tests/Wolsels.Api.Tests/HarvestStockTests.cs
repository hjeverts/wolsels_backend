using System.Net;
using System.Net.Http.Json;
using Wolsels.Api.Contracts;

namespace Wolsels.Api.Tests;

public class HarvestStockTests(WolselsFactory factory) : IClassFixture<WolselsFactory>
{
    private readonly HttpClient _client = factory.CreateUserClientAsync().GetAwaiter().GetResult();

    private async Task<int> AnimalAsync(string name) =>
        (await (await _client.PostAsJsonAsync("/api/animals", new { name, species = "Konijn", breed = "Duitse angora" }, TestJson.Options))
            .Content.ReadFromJsonAsync<AnimalDto>(TestJson.Options))!.Id;

    private async Task<int> LocationAsync(string name) =>
        (await (await _client.PostAsJsonAsync("/api/locations", new { name }, TestJson.Options))
            .Content.ReadFromJsonAsync<LocationDto>(TestJson.Options))!.Id;

    private async Task<HarvestDto> HarvestAsync(object body)
    {
        var response = await _client.PostAsJsonAsync("/api/harvests", body, TestJson.Options);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HarvestDto>(TestJson.Options))!;
    }

    private async Task<decimal> StockAsync(int woolId) =>
        (await _client.GetFromJsonAsync<WoolDto>($"/api/wools/{woolId}", TestJson.Options))!.WeightGrams;

    [Fact]
    public async Task HarvestsOfOneAnimal_CanBeCombinedInOneBatch()
    {
        var animal = await AnimalAsync("Flappie");
        var first = await HarvestAsync(new { date = "2030-01-10", animalId = animal, weightGrams = 80, addToStock = true });
        var second = await HarvestAsync(new { date = "2030-04-10", animalId = animal, weightGrams = 95.5, addToStock = true, stockWoolId = first.WoolId });

        Assert.Equal(first.WoolId, second.WoolId);
        Assert.Equal(175.5m, await StockAsync(first.WoolId!.Value));
        var built = await _client.GetFromJsonAsync<List<HarvestDto>>($"/api/harvests?woolId={first.WoolId}", TestJson.Options);
        Assert.Equal(2, built!.Count);
        Assert.All(built, h => Assert.Equal("Flappie 2030-01-10", h.WoolName));
    }

    [Fact]
    public async Task LocationHarvests_CanBeCombinedToo()
    {
        var location = await LocationAsync("Wei");
        var first = await HarvestAsync(new { date = "2030-05-01", locationId = location, weightGrams = 1000, addToStock = true });
        await HarvestAsync(new { date = "2030-06-01", locationId = location, weightGrams = 500, addToStock = true, stockWoolId = first.WoolId });
        Assert.Equal(1500m, await StockAsync(first.WoolId!.Value));
    }

    [Fact]
    public async Task ExistingHarvest_CanBeAddedLater_AndWeightChangesAreSynced()
    {
        var animal = await AnimalAsync("Snuf");
        var batch = await HarvestAsync(new { date = "2030-02-01", animalId = animal, weightGrams = 50, addToStock = true });
        var later = await HarvestAsync(new { date = "2030-03-01", animalId = animal, weightGrams = 40 });
        Assert.Null(later.WoolId);

        var added = await _client.PutAsJsonAsync($"/api/harvests/{later.Id}",
            new { date = "2030-03-01", animalId = animal, weightGrams = 40, addToStock = true, stockWoolId = batch.WoolId }, TestJson.Options);
        Assert.Equal(batch.WoolId, (await added.Content.ReadFromJsonAsync<HarvestDto>(TestJson.Options))!.WoolId);
        Assert.Equal(90m, await StockAsync(batch.WoolId!.Value));

        // Correcting the weight: the difference goes to the lot (and the full weight is not added again).
        await _client.PutAsJsonAsync($"/api/harvests/{later.Id}",
            new { date = "2030-03-01", animalId = animal, weightGrams = 45, addToStock = true, stockWoolId = batch.WoolId }, TestJson.Options);
        Assert.Equal(95m, await StockAsync(batch.WoolId.Value));
    }

    [Fact]
    public async Task DeletingHarvest_OptionallyRemovesItsWeightFromStock()
    {
        var animal = await AnimalAsync("Pluis");
        var a = await HarvestAsync(new { date = "2030-02-01", animalId = animal, weightGrams = 30, addToStock = true });
        var b = await HarvestAsync(new { date = "2030-02-02", animalId = animal, weightGrams = 20, addToStock = true, stockWoolId = a.WoolId });

        await _client.DeleteAsync($"/api/harvests/{b.Id}?removeFromStock=true");
        Assert.Equal(30m, await StockAsync(a.WoolId!.Value));

        await _client.DeleteAsync($"/api/harvests/{a.Id}");
        Assert.Equal(30m, await StockAsync(a.WoolId.Value));
    }

    [Fact]
    public async Task MergingBatches_MovesWeightHarvestsAndBlends()
    {
        var animal = await AnimalAsync("Wolkje");
        var old = await HarvestAsync(new { date = "2030-01-01", animalId = animal, weightGrams = 60, addToStock = true });
        var target = await HarvestAsync(new { date = "2030-02-01", animalId = animal, weightGrams = 40, addToStock = true });
        var blend = await (await _client.PostAsJsonAsync("/api/blends", new
        {
            name = "Met oude partij",
            components = new[] { new { woolId = old.WoolId, ratio = 1 } },
            basis = "TotalWeight",
            totalWeightGrams = 10,
        }, TestJson.Options)).Content.ReadFromJsonAsync<BlendDto>(TestJson.Options);

        var merged = await _client.PostAsJsonAsync($"/api/wools/{old.WoolId}/merge", new { targetWoolId = target.WoolId }, TestJson.Options);
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);

        Assert.Equal(100m, await StockAsync(target.WoolId!.Value));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/wools/{old.WoolId}")).StatusCode);
        var harvests = await _client.GetFromJsonAsync<List<HarvestDto>>($"/api/harvests?woolId={target.WoolId}", TestJson.Options);
        Assert.Equal(2, harvests!.Count);
        var updatedBlend = await _client.GetFromJsonAsync<BlendDto>($"/api/blends/{blend!.Id}", TestJson.Options);
        Assert.Equal(target.WoolId, Assert.Single(updatedBlend!.Components).WoolId);
    }

    [Fact]
    public async Task BatchOfAnotherHousehold_CannotBeUsed()
    {
        var other = await factory.CreateUserClientAsync();
        var foreignWool = (await (await other.PostAsJsonAsync("/api/wools",
                new { name = "Van een ander", woolType = "M", color = "W", weightGrams = 10 }, TestJson.Options))
            .Content.ReadFromJsonAsync<WoolDto>(TestJson.Options))!.Id;
        var animal = await AnimalAsync("Stoutje");

        var response = await _client.PostAsJsonAsync("/api/harvests",
            new { date = "2030-02-01", animalId = animal, weightGrams = 5, addToStock = true, stockWoolId = foreignWool }, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var merge = await _client.PostAsJsonAsync($"/api/wools/{foreignWool}/merge", new { targetWoolId = foreignWool + 1 }, TestJson.Options);
        Assert.Equal(HttpStatusCode.NotFound, merge.StatusCode);
        var ownStock = await other.GetFromJsonAsync<WoolDto>($"/api/wools/{foreignWool}", TestJson.Options);
        Assert.Equal(10m, ownStock!.WeightGrams);
    }
}
