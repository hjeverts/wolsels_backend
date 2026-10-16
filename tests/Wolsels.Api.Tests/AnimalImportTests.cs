using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Wolsels.Api.Contracts;

namespace Wolsels.Api.Tests;

public class AnimalImportTests(WolselsFactory factory) : IClassFixture<WolselsFactory>
{
    private static readonly string[] AllFields = ["species", "breed", "color", "birthDate", "location", "active", "notes"];

    private async Task<HttpClient> ClientAsync() => await factory.CreateUserClientAsync();

    private static async Task<AnimalImportResult> ImportAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/animals/import", body, TestJson.Options);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AnimalImportResult>(TestJson.Options))!;
    }

    private static async Task<List<AnimalDto>> AnimalsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<AnimalDto>>("/api/animals", TestJson.Options))!;

    [Fact]
    public async Task Import_CreatesAnimals_AndMissingLocations()
    {
        var client = await ClientAsync();
        await client.PostAsJsonAsync("/api/locations", new { name = "Stal" }, TestJson.Options);

        var result = await ImportAsync(client, new
        {
            fields = AllFields,
            rows = new object[]
            {
                new { name = "Flappie", species = "Konijn", breed = "Duitse angora", birthDate = "2024-03-01", location = "stal", notes = "Lief" },
                new { name = "Wolly", species = "Schaap", location = "Wei", active = false },
                new { name = "Bol", species = "Konijn", location = "wei" },
            },
        });

        Assert.Equal((3, 0, 0), (result.Created, result.Updated, result.Skipped));
        Assert.Equal(["Wei"], result.LocationsCreated);
        var animals = await AnimalsAsync(client);
        var flappie = animals.Single(a => a.Name == "Flappie");
        Assert.Equal("Stal", flappie.LocationName);
        Assert.Equal(new DateOnly(2024, 3, 1), flappie.BirthDate);
        Assert.True(flappie.Active);
        Assert.False(animals.Single(a => a.Name == "Wolly").Active);
        Assert.Equal("Wei", animals.Single(a => a.Name == "Bol").LocationName);
        var locations = await client.GetFromJsonAsync<List<LocationDto>>("/api/locations", TestJson.Options);
        Assert.Equal(["Stal", "Wei"], locations!.Select(l => l.Name));
    }

    [Fact]
    public async Task ExistingAnimals_AreSkippedByDefault_OrUpdatedOnlyInImportedFields()
    {
        var client = await ClientAsync();
        await ImportAsync(client, new
        {
            fields = AllFields,
            rows = new object[] { new { name = "Snuf", species = "Konijn", breed = "Franse angora", color = "Wit", location = "Hok 1" } },
        });

        var skipped = await ImportAsync(client, new
        {
            fields = new[] { "color" },
            rows = new object[] { new { name = "snuf", species = "Konijn", color = "Zwart" }, new { name = "Pluis", species = "Konijn" } },
        });
        Assert.Equal((1, 0, 1), (skipped.Created, skipped.Updated, skipped.Skipped));
        Assert.Equal("Wit", (await AnimalsAsync(client)).Single(a => a.Name == "Snuf").Color);

        var updated = await ImportAsync(client, new
        {
            updateExisting = true,
            fields = new[] { "color", "active" },
            rows = new object[] { new { name = "SNUF ", species = "Konijn", color = "Zwart", active = false } },
        });
        Assert.Equal((0, 1, 0), (updated.Created, updated.Updated, updated.Skipped));
        var snuf = (await AnimalsAsync(client)).Single(a => a.Name == "Snuf");
        Assert.Equal("Zwart", snuf.Color);
        Assert.False(snuf.Active);
        // Not in the file: unchanged.
        Assert.Equal("Franse angora", snuf.Breed);
        Assert.Equal("Hok 1", snuf.LocationName);
    }

    [Fact]
    public async Task UpdatingWithEmptyCells_ClearsFields_ButKeepsSpeciesAndActive()
    {
        var client = await ClientAsync();
        await ImportAsync(client, new
        {
            fields = AllFields,
            rows = new object[] { new { name = "Dot", species = "Alpaca", breed = "Huacaya", location = "Wei", active = false } },
        });

        await ImportAsync(client, new
        {
            updateExisting = true,
            fields = AllFields,
            rows = new object[] { new { name = "Dot", species = "", breed = "", location = "" } },
        });

        var dot = (await AnimalsAsync(client)).Single(a => a.Name == "Dot");
        Assert.Equal("Alpaca", dot.Species);
        Assert.Null(dot.Breed);
        Assert.Null(dot.LocationId);
        Assert.False(dot.Active);
    }

    [Fact]
    public async Task InvalidRows_SaveNothing_AndReportLineNumbers()
    {
        var client = await ClientAsync();
        var response = await client.PostAsJsonAsync("/api/animals/import", new
        {
            fields = AllFields,
            rows = new object[]
            {
                new { line = 2, name = "Goed", species = "Konijn", location = "Nieuwe stal" },
                new { line = 3, name = "", species = "Konijn" },
                new { line = 4, name = "Zonder soort", species = "" },
                new { line = 5, name = new string('x', 201), species = "Konijn" },
            },
        }, TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestJson.Options);
        var messages = problem!.Errors.Values.SelectMany(m => m).ToList();
        Assert.Contains("Regel 3: naam ontbreekt.", messages);
        Assert.Contains("Regel 5: naam is langer dan 200 tekens.", messages);
        Assert.Empty(await AnimalsAsync(client));
        Assert.Empty((await client.GetFromJsonAsync<List<LocationDto>>("/api/locations", TestJson.Options))!);

        // Species is only checked once the basic validation (name, lengths) passes.
        var noSpecies = await client.PostAsJsonAsync("/api/animals/import", new
        {
            rows = new object[] { new { line = 4, name = "Zonder soort", species = "" } },
        }, TestJson.Options);
        var speciesProblem = await noSpecies.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestJson.Options);
        Assert.Contains("Regel 4: diersoort ontbreekt.", speciesProblem!.Errors.Values.SelectMany(m => m));
    }

    [Fact]
    public async Task DefaultSpecies_IsOnlyUsedForNewAnimals()
    {
        var client = await ClientAsync();
        await client.PostAsJsonAsync("/api/animals", new { name = "Bestaand", species = "Alpaca" }, TestJson.Options);

        await ImportAsync(client, new
        {
            updateExisting = true,
            defaultSpecies = "Schaap",
            fields = new[] { "species" },
            rows = new object[] { new { name = "Bestaand", species = "" }, new { name = "Nieuw", species = "" }, new { name = "Eigen", species = "Geit" } },
        });

        var animals = await AnimalsAsync(client);
        Assert.Equal("Alpaca", animals.Single(a => a.Name == "Bestaand").Species);
        Assert.Equal("Schaap", animals.Single(a => a.Name == "Nieuw").Species);
        Assert.Equal("Geit", animals.Single(a => a.Name == "Eigen").Species);
    }

    [Fact]
    public async Task InvalidBirthDate_IsReportedWithLineNumber()
    {
        var client = await ClientAsync();
        var response = await client.PostAsJsonAsync("/api/animals/import", new
        {
            fields = AllFields,
            rows = new object[]
            {
                new { line = 41, name = "Goed", species = "Konijn", birthDate = "2024-02-29" },
                new { line = 42, name = "Fout", species = "Konijn", birthDate = "2024-02-30" },
                new { line = 43, name = "Ook fout", species = "Konijn", birthDate = "30-1-2024" },
                new { line = 44, name = "Leeg", species = "Konijn", birthDate = "" },
            },
        }, TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var messages = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestJson.Options))!
            .Errors.Values.SelectMany(m => m).ToList();
        Assert.Equal(
            ["Regel 42: geboortedatum \"2024-02-30\" is geen geldige datum (jjjj-mm-dd).",
             "Regel 43: geboortedatum \"30-1-2024\" is geen geldige datum (jjjj-mm-dd)."],
            messages);
    }

    [Fact]
    public async Task NullRow_IsAValidationError()
    {
        var client = await ClientAsync();
        var response = await client.PostAsJsonAsync("/api/animals/import",
            new { rows = new object?[] { new { name = "A", species = "Konijn" }, null } }, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AmbiguousUpdates_AreRejected()
    {
        var client = await ClientAsync();
        await client.PostAsJsonAsync("/api/animals", new { name = "Twee", species = "Konijn" }, TestJson.Options);
        await client.PostAsJsonAsync("/api/animals", new { name = "Twee", species = "Konijn" }, TestJson.Options);
        await client.PostAsJsonAsync("/api/animals", new { name = "Een", species = "Konijn" }, TestJson.Options);

        var response = await client.PostAsJsonAsync("/api/animals/import", new
        {
            updateExisting = true,
            fields = new[] { "color" },
            rows = new object[]
            {
                new { line = 2, name = "Twee", color = "Grijs" },
                new { line = 3, name = "Een", color = "Wit" },
                new { line = 4, name = "een", color = "Zwart" },
            },
        }, TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var messages = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestJson.Options))!
            .Errors.Values.SelectMany(m => m).ToList();
        Assert.Contains(messages, m => m.StartsWith("Regel 2: er zijn meerdere dieren met de naam"));
        Assert.Contains("Regel 3: \"Een\" staat meerdere keren in het bestand.", messages);
        Assert.All(await AnimalsAsync(client), a => Assert.Null(a.Color));
    }

    [Fact]
    public async Task ImportOnlyTouchesOwnHousehold()
    {
        var other = await ClientAsync();
        await other.PostAsJsonAsync("/api/animals", new { name = "Buur", species = "Konijn", color = "Bruin" }, TestJson.Options);
        await other.PostAsJsonAsync("/api/locations", new { name = "Schuur" }, TestJson.Options);

        var client = await ClientAsync();
        var result = await ImportAsync(client, new
        {
            updateExisting = true,
            fields = new[] { "color", "location" },
            rows = new object[] { new { name = "Buur", species = "Konijn", color = "Wit", location = "Schuur" } },
        });

        Assert.Equal((1, 0), (result.Created, result.Updated));
        Assert.Equal(["Schuur"], result.LocationsCreated);
        Assert.Equal("Bruin", (await AnimalsAsync(other)).Single().Color);
    }

    [Fact]
    public async Task EmptyImport_AndUnknownFields_AreRejected()
    {
        var client = await ClientAsync();
        var empty = await client.PostAsJsonAsync("/api/animals/import", new { rows = Array.Empty<object>() }, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var unknown = await client.PostAsJsonAsync("/api/animals/import",
            new { fields = new[] { "gewicht" }, rows = new object[] { new { name = "A", species = "Konijn" } } }, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }
}
