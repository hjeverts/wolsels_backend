using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Wolsels.Api.Contracts;

namespace Wolsels.Api.Tests;

public class HouseholdTests(WolselsFactory factory) : IClassFixture<WolselsFactory>
{
    private static readonly JsonSerializerOptions Json = TestJson.Options;

    private static async Task<int> AddWoolAsync(HttpClient client, string name) =>
        (await (await client.PostAsJsonAsync("/api/wools", new { name, woolType = "Merino", color = "Wit", weightGrams = 100 }, Json))
            .Content.ReadFromJsonAsync<WoolDto>(Json))!.Id;

    private static async Task<List<string>> WoolNamesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<WoolDto>>("/api/wools", Json))!.Select(w => w.Name).ToList();

    private static async Task<string> InviteAsync(HttpClient client) =>
        (await (await client.PostAsync("/api/household/invites", null)).Content.ReadFromJsonAsync<CreatedInviteDto>(Json))!.Token;

    private static async Task<JoinedDto> JoinAsync(HttpClient client, string token)
    {
        var response = await client.PostAsJsonAsync("/api/household/join", new { token }, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JoinedDto>(Json))!;
    }

    private static void UseHousehold(HttpClient client, int householdId)
    {
        client.DefaultRequestHeaders.Remove("X-Household");
        client.DefaultRequestHeaders.Add("X-Household", householdId.ToString());
    }

    [Fact]
    public async Task InvitedPartner_SharesTheHousehold_AndKeepsOwnHousehold()
    {
        var hans = await factory.CreateUserClientAsync();
        var partner = await factory.CreateUserClientAsync();
        await AddWoolAsync(hans, "Gedeelde merino");
        await AddWoolAsync(partner, "Eigen alpaca");

        var invite = await InviteAsync(hans);
        var info = await factory.CreateClient().GetFromJsonAsync<InviteInfoDto>($"/api/household/invites/{invite}", Json);
        Assert.StartsWith("Huishouden van", info!.HouseholdName);

        var joined = await JoinAsync(partner, invite);

        var partnerHouseholds = await partner.GetFromJsonAsync<List<HouseholdSummaryDto>>("/api/households", Json);
        Assert.Equal(2, partnerHouseholds!.Count);

        // Standaard (zonder header) het eigen, eerste huishouden; met header het gedeelde.
        Assert.Equal(["Eigen alpaca"], await WoolNamesAsync(partner));
        UseHousehold(partner, joined.HouseholdId);
        Assert.Equal(["Gedeelde merino"], await WoolNamesAsync(partner));

        // Wat de partner toevoegt in het gedeelde huishouden, ziet hans ook.
        await AddWoolAsync(partner, "Samen gekocht");
        Assert.Contains("Samen gekocht", await WoolNamesAsync(hans));
        Assert.DoesNotContain("Eigen alpaca", await WoolNamesAsync(hans));

        var household = await hans.GetFromJsonAsync<HouseholdDto>("/api/household", Json);
        Assert.Equal(2, household!.Members.Count);
    }

    [Fact]
    public async Task InviteWorksOnlyOnce_AndCanBeRevoked()
    {
        var owner = await factory.CreateUserClientAsync();
        var first = await factory.CreateUserClientAsync();
        var second = await factory.CreateUserClientAsync();

        var token = await InviteAsync(owner);
        await JoinAsync(first, token);
        var reused = await second.PostAsJsonAsync("/api/household/join", new { token }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);

        var revokedToken = await InviteAsync(owner);
        var household = await owner.GetFromJsonAsync<HouseholdDto>("/api/household", Json);
        var inviteId = Assert.Single(household!.Invites).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/household/invites/{inviteId}")).StatusCode);
        var afterRevoke = await second.PostAsJsonAsync("/api/household/join", new { token = revokedToken }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, afterRevoke.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.CreateClient().GetAsync("/api/household/invites/onzin-token")).StatusCode);
    }

    [Fact]
    public async Task ForeignHouseholdHeader_IsRejected()
    {
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();
        var aliceHousehold = (await alice.GetFromJsonAsync<HouseholdDto>("/api/household", Json))!.Id;
        await AddWoolAsync(alice, "Geheim");

        UseHousehold(bob, aliceHousehold);
        var response = await bob.GetAsync("/api/wools");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("household-access", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RemovedMember_LosesAccessImmediately()
    {
        var owner = await factory.CreateUserClientAsync();
        var member = await factory.CreateUserClientAsync();
        await AddWoolAsync(owner, "Van de eigenaar");
        var joined = await JoinAsync(member, await InviteAsync(owner));
        UseHousehold(member, joined.HouseholdId);
        Assert.Contains("Van de eigenaar", await WoolNamesAsync(member));

        var household = await owner.GetFromJsonAsync<HouseholdDto>("/api/household", Json);
        var memberId = household!.Members.Single(m => !m.IsYou).UserId;
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/household/members/{memberId}")).StatusCode);

        // Zelfde sessie, zelfde header: direct geweigerd.
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/wools")).StatusCode);
    }

    [Fact]
    public async Task LastMemberCannotLeave_OthersCan()
    {
        var owner = await factory.CreateUserClientAsync();
        var member = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync("/api/household/leave", null)).StatusCode);

        var joined = await JoinAsync(member, await InviteAsync(owner));
        UseHousehold(member, joined.HouseholdId);
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsync("/api/household/leave", null)).StatusCode);

        // Het verlaten huishouden is niet meer toegankelijk; accountzaken en de lijst blijven werken.
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/wools")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/auth/me")).StatusCode);
        var remaining = await member.GetFromJsonAsync<List<HouseholdSummaryDto>>("/api/households", Json);
        Assert.Single(remaining!);
    }

    [Fact]
    public async Task ExtraHousehold_CanBeCreated()
    {
        var user = await factory.CreateUserClientAsync();
        await AddWoolAsync(user, "In eerste");
        var created = await (await user.PostAsJsonAsync("/api/households", new { name = "Spinclub" }, Json))
            .Content.ReadFromJsonAsync<HouseholdSummaryDto>(Json);

        UseHousehold(user, created!.Id);
        Assert.Empty(await WoolNamesAsync(user));
        var households = await user.GetFromJsonAsync<List<HouseholdSummaryDto>>("/api/households", Json);
        Assert.Contains(households!, h => h.Name == "Spinclub");
    }
}

public class InviteRegistrationTests(ClosedRegistrationFactory factory) : IClassFixture<ClosedRegistrationFactory>
{
    [Fact]
    public async Task WithInvite_RegistrationWorksEvenWhenClosed()
    {
        var owner = await factory.CreateUserClientAsync("eerste@example.nl");
        var token = (await (await owner.PostAsync("/api/household/invites", null))
            .Content.ReadFromJsonAsync<CreatedInviteDto>(TestJson.Options))!.Token;

        var anonymous = factory.CreateClient();
        var withoutInvite = await anonymous.PostAsJsonAsync("/api/auth/register",
            new { email = "zonder@example.nl", password = TestJson.Password });
        Assert.Equal(HttpStatusCode.Forbidden, withoutInvite.StatusCode);

        var withInvite = await anonymous.PostAsJsonAsync($"/api/auth/register?invite={token}",
            new { email = "partner@example.nl", password = TestJson.Password });
        Assert.Equal(HttpStatusCode.OK, withInvite.StatusCode);
    }
}
