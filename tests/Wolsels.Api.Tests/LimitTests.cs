using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace Wolsels.Api.Tests;

public class PasswordResetTests(WolselsFactory factory) : IClassFixture<WolselsFactory>
{
    [Fact]
    public async Task ForgotPassword_SendsWorkingLink_ThatCanBeUsedOnce()
    {
        var email = $"{Guid.NewGuid():N}@example.nl";
        await factory.CreateUserClientAsync(email);
        var anonymous = factory.CreateClient();

        var forgot = await anonymous.PostAsJsonAsync("/api/auth/password/forgot", new { email });
        Assert.Equal(HttpStatusCode.NoContent, forgot.StatusCode);

        var mail = Assert.Single(factory.Mail.Sent, m => m.To == email);
        var link = Regex.Match(mail.Text, @"https://wol\.example\.nl/wachtwoord-herstellen\?email=[^&]+&code=(\S+)");
        Assert.True(link.Success, mail.Text);
        var code = link.Groups[1].Value;

        var reset = await anonymous.PostAsJsonAsync("/api/auth/password/reset",
            new { email, code, newPassword = "nieuw wachtwoord na herstel" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = "nieuw wachtwoord na herstel" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // De link is na gebruik ongeldig (security stamp is veranderd).
        var again = await anonymous.PostAsJsonAsync("/api/auth/password/reset",
            new { email, code, newPassword = "nog een ander wachtwoord" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_DoesNotRevealWhetherAccountExists()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/password/forgot",
            new { email = "bestaat-niet@example.nl" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(factory.Mail.Sent, m => m.To == "bestaat-niet@example.nl");
    }

    [Fact]
    public async Task ForgotPassword_SendsAtMostThreeMailsPerAddressPerHour()
    {
        var email = $"{Guid.NewGuid():N}@example.nl";
        await factory.CreateUserClientAsync(email);

        for (var i = 0; i < 6; i++)
        {
            var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/password/forgot", new { email });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(3, factory.Mail.Sent.Count(m => m.To == email));
    }

    [Fact]
    public async Task InvalidResetCode_IsRejected()
    {
        var email = $"{Guid.NewGuid():N}@example.nl";
        await factory.CreateUserClientAsync(email);
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/password/reset",
            new { email, code = "dit-is-geen-geldige-code", newPassword = "wat dan ook lang genoeg" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public sealed class NoPublicUrlFactory : WolselsFactory
{
    protected override void Configure(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.Configure(builder);
        builder.UseSetting("App:PublicUrl", "");
    }
}

public class PasswordResetWithoutPublicUrlTests(NoPublicUrlFactory factory) : IClassFixture<NoPublicUrlFactory>
{
    [Fact]
    public async Task ResetLinks_AreNeverBuiltFromTheHostHeader()
    {
        var email = $"{Guid.NewGuid():N}@example.nl";
        await factory.CreateUserClientAsync(email);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "aanvaller.example";

        var response = await client.PostAsJsonAsync("/api/auth/password/forgot", new { email });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Mail.Sent);
        var options = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/options");
        Assert.False(options.GetProperty("passwordResetAvailable").GetBoolean());
    }
}

/// <summary>Draait met lage limieten, zodat de begrenzing zelf getest kan worden.</summary>
public sealed class StrictLimitsFactory : WolselsFactory
{
    protected override void Configure(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:RateLimitPerMinute", "4");
        builder.UseSetting("Auth:RateLimitPerHour", "100");
        builder.UseSetting("Auth:PasswordResetPerIpPerHour", "2");
    }
}

public class RateLimitTests(StrictLimitsFactory factory) : IClassFixture<StrictLimitsFactory>
{
    [Fact]
    public async Task LoginAttempts_AreLimitedPerClient_ButNormalApiIsNot()
    {
        var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login?useCookies=true",
                new { email = $"raden{i}@example.nl", password = "geprobeerd wachtwoord" });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);

        // Niet-gevoelige endpoints vallen er niet onder.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/options")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }
}
