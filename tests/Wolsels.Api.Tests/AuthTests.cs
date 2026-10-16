using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Wolsels.Api.Contracts;

namespace Wolsels.Api.Tests;

public class AuthTests(WolselsFactory factory) : IClassFixture<WolselsFactory>
{
    private static readonly JsonSerializerOptions Json = TestJson.Options;

    [Theory]
    [InlineData("/api/wools")]
    [InlineData("/api/blends")]
    [InlineData("/api/harvests/summary")]
    [InlineData("/api/settings")]
    [InlineData("/api/auth/me")]
    public async Task Api_RequiresLogin(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_IsPublic()
    {
        var response = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Users_OnlySeeTheirOwnData()
    {
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();

        var created = await alice.PostAsJsonAsync("/api/wools",
            new { name = "Van Alice", woolType = "Merino", color = "Wit", weightGrams = 100 }, Json);
        var wool = (await created.Content.ReadFromJsonAsync<WoolDto>(Json))!;

        Assert.DoesNotContain(await bob.GetFromJsonAsync<List<WoolDto>>("/api/wools", Json) ?? [], w => w.Id == wool.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/wools/{wool.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/wools/{wool.Id}",
            new { name = "Gestolen", woolType = "Merino", color = "Wit", weightGrams = 0 }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/wools/{wool.Id}")).StatusCode);

        var blend = await bob.PostAsJsonAsync("/api/blends", new
        {
            name = "Met wol van een ander",
            components = new[] { new { woolId = wool.Id, ratio = 1 } },
            basis = "TotalWeight",
            totalWeightGrams = 50,
        }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, blend.StatusCode);

        var stillThere = await alice.GetFromJsonAsync<WoolDto>($"/api/wools/{wool.Id}", Json);
        Assert.Equal("Van Alice", stillThere!.Name);
    }

    [Fact]
    public async Task Logout_EndsSession()
    {
        var client = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_KeepsSessionAndRequiresNewPassword()
    {
        var email = $"{Guid.NewGuid():N}@example.nl";
        var client = await factory.CreateUserClientAsync(email);

        var wrong = await client.PostAsJsonAsync("/api/auth/password",
            new { oldPassword = "niet het wachtwoord", newPassword = "een heel nieuw wachtwoord" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var changed = await client.PostAsJsonAsync("/api/auth/password",
            new { oldPassword = TestJson.Password, newPassword = "een heel nieuw wachtwoord" });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var oldLogin = await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = TestJson.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var newLogin = await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = "een heel nieuw wachtwoord" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task ShortPassword_IsRejectedInDutch()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = "kort@example.nl", password = "kort" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("minimaal 10 tekens", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TwoFactor_CanBeEnabledAndIsRequiredAtLogin()
    {
        var email = $"{Guid.NewGuid():N}@example.nl";
        var client = await factory.CreateUserClientAsync(email);

        // Step 1: get the shared key (for the QR code), step 2: enable with a code from the app.
        var setup = await (await client.PostAsJsonAsync("/api/auth/manage/2fa", new { }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var sharedKey = setup.GetProperty("sharedKey").GetString()!;
        var enable = await client.PostAsJsonAsync("/api/auth/manage/2fa",
            new { enable = true, twoFactorCode = Totp.Code(sharedKey) });
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var recoveryCodes = (await enable.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.NotEmpty(recoveryCodes);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(me.GetProperty("twoFactorEnabled").GetBoolean());

        // Without a code: rejected with the message that 2FA is required.
        var fresh = factory.CreateClient();
        var withoutCode = await fresh.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = TestJson.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, withoutCode.StatusCode);
        Assert.Contains("RequiresTwoFactor", await withoutCode.Content.ReadAsStringAsync());

        var wrongCode = await fresh.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = TestJson.Password, twoFactorCode = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCode.StatusCode);

        var withCode = await fresh.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = TestJson.Password, twoFactorCode = Totp.Code(sharedKey) });
        Assert.Equal(HttpStatusCode.OK, withCode.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/wools")).StatusCode);

        // A recovery code works too, but only once.
        var recovery = factory.CreateClient();
        var withRecovery = await recovery.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = TestJson.Password, twoFactorRecoveryCode = recoveryCodes[0] });
        Assert.Equal(HttpStatusCode.OK, withRecovery.StatusCode);
        var reused = await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = TestJson.Password, twoFactorRecoveryCode = recoveryCodes[0] });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task RepeatedWrongPasswords_LockTheAccount()
    {
        var email = $"{Guid.NewGuid():N}@example.nl";
        await factory.CreateUserClientAsync(email);
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = "fout fout fout" });

        var correct = await client.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = TestJson.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
        Assert.Contains("LockedOut", await correct.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnusedIdentityEndpoints_AreDisabled()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/forgotPassword", new { email = "x@example.nl" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public sealed class ClosedRegistrationFactory : WolselsFactory
{
    protected override bool AllowRegistration => false;
}

public class ClosedRegistrationTests(ClosedRegistrationFactory factory) : IClassFixture<ClosedRegistrationFactory>
{
    [Fact]
    public async Task OnlyTheFirstAccountCanRegister()
    {
        var anonymous = factory.CreateClient();
        var before = await anonymous.GetFromJsonAsync<JsonElement>("/api/auth/options");
        Assert.True(before.GetProperty("allowRegistration").GetBoolean());

        await factory.CreateUserClientAsync("beheerder@example.nl");

        var after = await anonymous.GetFromJsonAsync<JsonElement>("/api/auth/options");
        Assert.False(after.GetProperty("allowRegistration").GetBoolean());
        var second = await anonymous.PostAsJsonAsync("/api/auth/register",
            new { email = "tweede@example.nl", password = TestJson.Password });
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }
}
