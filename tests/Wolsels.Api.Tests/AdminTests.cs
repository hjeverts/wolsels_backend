using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wolsels.Api.Contracts;

namespace Wolsels.Api.Tests;

public sealed class AdminFactory : WolselsFactory;

public class AdminTests(AdminFactory factory) : IClassFixture<AdminFactory>
{
    private static readonly JsonSerializerOptions Json = TestJson.Options;
    private static readonly SemaphoreSlim Setup = new(1, 1);
    private static HttpClient? _admin;

    /// <summary>The very first account of this test instance (and therefore administrator).</summary>
    private async Task<HttpClient> AdminAsync()
    {
        await Setup.WaitAsync();
        try
        {
            return _admin ??= await factory.CreateUserClientAsync("beheerder@example.nl");
        }
        finally
        {
            Setup.Release();
        }
    }

    private async Task<AdminUserDto> FindAsync(HttpClient admin, string email) =>
        (await admin.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users", Json))!.Single(u => u.Email == email);

    [Fact]
    public async Task FirstAccountIsAdmin_OthersAreNot()
    {
        var admin = await AdminAsync();
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(me.GetProperty("isAdmin").GetBoolean());

        var normal = await factory.CreateUserClientAsync();
        var normalMe = await normal.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.False(normalMe.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await normal.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task BlockedUser_LosesSessionImmediately_AndCannotLogIn()
    {
        var admin = await AdminAsync();
        var email = $"{Guid.NewGuid():N}@example.nl";
        var user = await factory.CreateUserClientAsync(email);
        var target = await FindAsync(admin, email);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{target.Id}/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/wools")).StatusCode);
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = TestJson.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.True((await FindAsync(admin, email)).Blocked);

        await admin.PostAsync($"/api/admin/users/{target.Id}/unblock", null);
        var again = await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = TestJson.Password });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task AdminCanReset2FA_AndCreateResetLink()
    {
        var admin = await AdminAsync();
        var email = $"{Guid.NewGuid():N}@example.nl";
        var user = await factory.CreateUserClientAsync(email);
        var key = (await (await user.PostAsJsonAsync("/api/auth/manage/2fa", new { })).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sharedKey").GetString()!;
        await user.PostAsJsonAsync("/api/auth/manage/2fa", new { enable = true, twoFactorCode = Totp.Code(key) });
        var target = await FindAsync(admin, email);
        Assert.True(target.TwoFactorEnabled);

        await admin.PostAsync($"/api/admin/users/{target.Id}/reset-2fa", null);
        Assert.False((await FindAsync(admin, email)).TwoFactorEnabled);
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = TestJson.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var link = await (await admin.PostAsync($"/api/admin/users/{target.Id}/password-reset", null))
            .Content.ReadFromJsonAsync<ResetLinkDto>(Json);
        var code = Regex.Match(link!.Path, "code=([^&]+)").Groups[1].Value;
        var reset = await factory.CreateClient().PostAsJsonAsync("/api/auth/password/reset",
            new { email, code, newPassword = "door beheerder hersteld" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    }

    [Fact]
    public async Task LastAdmin_CannotBeDemotedOrDeleted()
    {
        var admin = await AdminAsync();
        var me = await FindAsync(admin, "beheerder@example.nl");

        var demote = await admin.PutAsJsonAsync($"/api/admin/users/{me.Id}/admin", new { isAdmin = false }, Json);
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/admin/users/{me.Id}")).StatusCode);

        // Making a second administrator and revoking it again is fine; revoking takes effect immediately.
        var email = $"{Guid.NewGuid():N}@example.nl";
        var other = await factory.CreateUserClientAsync(email);
        var target = await FindAsync(admin, email);
        await admin.PutAsJsonAsync($"/api/admin/users/{target.Id}/admin", new { isAdmin = true }, Json);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/admin/users")).StatusCode);
        await admin.PutAsJsonAsync($"/api/admin/users/{target.Id}/admin", new { isAdmin = false }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task DeletingUser_RemovesOnlyHouseholdsTheyAloneBelongTo()
    {
        var admin = await AdminAsync();
        var email = $"{Guid.NewGuid():N}@example.nl";
        var leaving = await factory.CreateUserClientAsync(email);
        var partner = await factory.CreateUserClientAsync();

        await leaving.PostAsJsonAsync("/api/wools", new { name = "Alleen van mij", woolType = "M", color = "W", weightGrams = 1 }, Json);
        // Shared household: the partner's, with the departing user as a member.
        await partner.PostAsJsonAsync("/api/wools", new { name = "Gedeeld", woolType = "M", color = "W", weightGrams = 1 }, Json);
        var token = (await (await partner.PostAsync("/api/household/invites", null)).Content.ReadFromJsonAsync<CreatedInviteDto>(Json))!.Token;
        await leaving.PostAsJsonAsync("/api/household/join", new { token }, Json);

        var target = await FindAsync(admin, email);
        var preview = await admin.GetFromJsonAsync<DeletePreviewDto>($"/api/admin/users/{target.Id}/delete-preview", Json);
        var deleted = Assert.Single(preview!.HouseholdsDeleted);
        Assert.Equal(1, deleted.Items);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/users/{target.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await leaving.GetAsync("/api/wools")).StatusCode);
        var shared = await partner.GetFromJsonAsync<List<WoolDto>>("/api/wools", Json);
        Assert.Contains(shared!, w => w.Name == "Gedeeld");
        var household = await partner.GetFromJsonAsync<HouseholdDto>("/api/household", Json);
        Assert.Single(household!.Members);
    }
}
