using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Wolsels.Api.Contracts;
using Wolsels.Api.Domain;

namespace Wolsels.Api.Tests;

public sealed class WolselsFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"wolsels-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Wolsels", $"Data Source={_dbPath};Pooling=False");
        builder.UseSetting("Blends:MaxComponents", "3");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        File.Delete(_dbPath);
    }
}

public class ApiTests(WolselsFactory factory) : IClassFixture<WolselsFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<WoolDto> CreateWoolAsync(string name, decimal weight = 500)
    {
        var response = await _client.PostAsJsonAsync("/api/wools",
            new { name, woolType = "Merino", color = "Naturel", colorHex = "#f5f0e6", weightGrams = weight }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WoolDto>(Json))!;
    }

    [Fact]
    public async Task Settings_ExposeMaxComponents()
    {
        var settings = await _client.GetFromJsonAsync<SettingsDto>("/api/settings", Json);
        Assert.Equal(3, settings!.MaxBlendComponents);
    }

    [Fact]
    public async Task Blend_IsCalculatedAndStoredWithSpinningResult()
    {
        var merino = await CreateWoolAsync("Merino wit");
        var alpaca = await CreateWoolAsync("Alpaca grijs");

        var response = await _client.PostAsJsonAsync("/api/blends", new
        {
            name = "Wolkje",
            components = new[] { new { woolId = merino.Id, ratio = 70 }, new { woolId = alpaca.Id, ratio = 30 } },
            basis = "ComponentWeight",
            basisComponentIndex = 1,
            basisComponentWeightGrams = 60,
            status = "Spun",
            outputWeightGrams = 190,
            yarnWeight = "DK",
            plyCount = 2,
            plyMethod = "2-draads",
            spinDirection = "Z",
            isDyed = true,
            dyeColor = "Oceaanblauw",
            dyeColorHex = "#1E5A8A",
            dyeMethod = "Zuurverf, 45 min op 85 °C",
        }, Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var blend = (await response.Content.ReadFromJsonAsync<BlendDto>(Json))!;
        Assert.Equal(200m, blend.InputWeightGrams);
        Assert.Equal([140m, 60m], blend.Components.Select(c => c.WeightGrams));
        Assert.Equal("Merino wit", blend.Components[0].WoolName);
        Assert.Equal(95m, blend.YieldPercentage);
        Assert.Equal("#1e5a8a", blend.DyeColorHex);
        Assert.Equal(BlendStatus.Spun, blend.Status);
    }

    [Fact]
    public async Task Blend_RejectsTooManyComponents()
    {
        var wool = await CreateWoolAsync("Veel");
        var response = await _client.PostAsJsonAsync("/api/blends", new
        {
            name = "Te veel",
            components = Enumerable.Range(0, 4).Select(_ => new { woolId = wool.Id, ratio = 1 }),
            basis = "TotalWeight",
            totalWeightGrams = 100,
        }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WoolUsedInBlend_CannotBeDeleted()
    {
        var wool = await CreateWoolAsync("In gebruik");
        await _client.PostAsJsonAsync("/api/blends", new
        {
            name = "Puur",
            components = new[] { new { woolId = wool.Id, ratio = 1 } },
            basis = "TotalWeight",
            totalWeightGrams = 100,
        }, Json);

        var response = await _client.DeleteAsync($"/api/wools/{wool.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Harvests_OnAnimalAndLocation_AreSummarised()
    {
        var location = await (await _client.PostAsJsonAsync("/api/locations", new { name = "Konijnenschuur" }, Json))
            .Content.ReadFromJsonAsync<LocationDto>(Json);
        var animal = await (await _client.PostAsJsonAsync("/api/animals",
                new { name = "Flappie", species = "Konijn", breed = "Duitse angora", color = "Wit", locationId = location!.Id }, Json))
            .Content.ReadFromJsonAsync<AnimalDto>(Json);

        var onAnimal = await _client.PostAsJsonAsync("/api/harvests",
            new { date = "2030-03-01", animalId = animal!.Id, weightGrams = 85.5, method = "Plukken", addToStock = true }, Json);
        Assert.Equal(HttpStatusCode.Created, onAnimal.StatusCode);
        var harvest = (await onAnimal.Content.ReadFromJsonAsync<HarvestDto>(Json))!;
        Assert.NotNull(harvest.WoolId);

        var stock = await _client.GetFromJsonAsync<WoolDto>($"/api/wools/{harvest.WoolId}", Json);
        Assert.Equal(85.5m, stock!.WeightGrams);
        Assert.Equal("Duitse angora", stock.WoolType);

        await _client.PostAsJsonAsync("/api/harvests",
            new { date = "2030-04-15", locationId = location.Id, weightGrams = 200 }, Json);

        var summary = await _client.GetFromJsonAsync<ProductionSummaryDto>("/api/harvests/summary?year=2030", Json);
        Assert.Equal(285.5m, summary!.TotalGrams);
        Assert.Equal(2, summary.PerMonth.Count);
        Assert.Contains(summary.PerSource, s => s is { Kind: "animal", Name: "Flappie", TotalGrams: 85.5m });

        var updatedAnimal = await _client.GetFromJsonAsync<AnimalDto>($"/api/animals/{animal.Id}", Json);
        Assert.Equal(85.5m, updatedAnimal!.TotalHarvestedGrams);

        var deleteAnimal = await _client.DeleteAsync($"/api/animals/{animal.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteAnimal.StatusCode);
    }

    [Fact]
    public async Task Harvest_RequiresExactlyOneSource()
    {
        var response = await _client.PostAsJsonAsync("/api/harvests",
            new { date = "2030-03-01", weightGrams = 10 }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
