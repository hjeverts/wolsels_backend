using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Wolsels.Api.Localization;

namespace Wolsels.Api.Tests;

public partial class LocalizationTests(WolselsFactory factory) : IClassFixture<WolselsFactory>
{
    private sealed record Me(string Email, string? Language);

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();

    [Fact]
    public void EveryLanguage_HasTheSameTextsAndPlaceholdersAsDutch()
    {
        var dutch = Texts.AllTexts[Texts.DefaultLanguage];
        foreach (var language in Texts.Languages)
        {
            var texts = Texts.AllTexts[language];
            Assert.Equal(dutch.Keys.Order(), texts.Keys.Order());
            foreach (var (key, text) in dutch)
            {
                var expected = Placeholder().Matches(text).Select(m => m.Value).Order();
                Assert.True(expected.SequenceEqual(Placeholder().Matches(texts[key]).Select(m => m.Value).Order()),
                    $"{language}: {key} has different parameters than the Dutch text.");
            }
        }
    }

    [Fact]
    public void EveryText_CanBeFormattedWithoutErrors()
    {
        // A stray { or } in a translation would only cause a FormatException (500) when used.
        var args = Enumerable.Range(0, 5).Select(i => (object)(i + 0.5m)).ToArray();
        foreach (var language in Texts.Languages)
        foreach (var key in Texts.AllTexts[language].Keys)
        {
            var exception = Record.Exception(() => Texts.GetFor(language, key, args));
            Assert.True(exception is null, $"{language}: {key}: {exception?.Message}");
        }
    }

    [Theory]
    [InlineData("de;q=abc,,,")]
    [InlineData("\"<script>\"")]
    [InlineData("xx-YY, zz;q=0.5")]
    public async Task MalformedOrUnknownAcceptLanguage_FallsBackToDutch(string header)
    {
        var client = await factory.CreateUserClientAsync();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", header);
        var response = await client.PostAsJsonAsync("/api/harvests", new { date = "2026-01-01", weightGrams = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(Texts.GetFor("nl", "Harvests.ChooseSource"), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AcceptLanguage_UsesTheHighestQualitySupportedLanguage()
    {
        var client = await factory.CreateUserClientAsync();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ja;q=1, fr;q=0.4, de;q=0.8");
        var response = await client.PostAsJsonAsync("/api/harvests", new { date = "2026-01-01", weightGrams = 10 });
        Assert.Contains(Texts.GetFor("de", "Harvests.ChooseSource"), await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("de-AT", "de")]
    [InlineData("no", "nb")]
    [InlineData("pt-BR", "pt")]
    [InlineData("ja", null)]
    [InlineData("", null)]
    public void Normalize_MapsBrowserTagsToSupportedLanguages(string tag, string? expected) =>
        Assert.Equal(expected, Texts.Normalize(tag));

    [Fact]
    public async Task Language_IsStoredInTheProfile()
    {
        var client = await factory.CreateUserClientAsync();
        Assert.Null((await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Language);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/auth/language", new { language = "de" })).StatusCode);
        Assert.Equal("de", (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Language);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/auth/language", new { language = "pl" })).StatusCode);
        Assert.Equal("pl", (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Language);
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("DE")]
    [InlineData("<script>")]
    [InlineData(null)]
    public async Task Language_RejectsUnknownLanguages(string? language)
    {
        var client = await factory.CreateUserClientAsync();
        var response = await client.PutAsJsonAsync("/api/auth/language", new { language });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Language);
    }

    [Fact]
    public async Task Language_RequiresLogin()
    {
        var response = await factory.CreateClient().PutAsJsonAsync("/api/auth/language", new { language = "de" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Messages_FollowTheAcceptLanguageHeader()
    {
        var client = await factory.CreateUserClientAsync();
        var harvest = new { date = "2026-01-01", weightGrams = 10 };

        var dutch = await (await client.PostAsJsonAsync("/api/harvests", harvest)).Content.ReadAsStringAsync();
        Assert.Contains(Texts.GetFor("nl", "Harvests.ChooseSource"), dutch);

        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-GB,en;q=0.9");
        var english = await (await client.PostAsJsonAsync("/api/harvests", harvest)).Content.ReadAsStringAsync();
        Assert.Contains(Texts.GetFor("en", "Harvests.ChooseSource"), english);

        // Messages from validation attributes too.
        var wool = await client.PostAsJsonAsync("/api/wools",
            new { name = "Test", woolType = "Merino", color = "Wit", colorHex = "rood", weightGrams = 1 });
        Assert.Contains(Texts.GetFor("en", "Validation.ColorHex"), await wool.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PasswordResetMail_UsesTheProfileLanguage()
    {
        var email = $"{Guid.NewGuid():N}@example.nl";
        var client = await factory.CreateUserClientAsync(email);
        await client.PutAsJsonAsync("/api/auth/language", new { language = "fr" });

        var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de");
        await anonymous.PostAsJsonAsync("/api/auth/password/forgot", new { email });

        var mail = Assert.Single(factory.Mail.Sent, m => m.To == email);
        Assert.Equal(Texts.GetFor("fr", "PasswordReset.MailSubject"), mail.Subject);
    }
}
