using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Admin;
using Wolsels.Api.Auth;
using Wolsels.Api.Contracts;
using Wolsels.Api.Data;
using Wolsels.Api.Households;
using Wolsels.Api.Services;

namespace Wolsels.Api.Controllers;

/// <summary>
/// Gebruikersbeheer voor beheerders. Bewust geen toegang tot de gegevens (wol, mengsels, ...) van huishoudens.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = AdminAuthorization.Policy)]
public class AdminController(
    UserManager<IdentityUser> users, WolselsDbContext db, HouseholdService households,
    ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    private string MyId => currentUser.UserId!;

    [HttpGet]
    public async Task<IEnumerable<AdminUserDto>> List(CancellationToken ct)
    {
        var all = await users.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
        var admins = (await users.GetUsersInRoleAsync(AdminAuthorization.Role)).Select(u => u.Id).ToHashSet();
        var memberships = await db.HouseholdMembers.AsNoTracking()
            .Select(m => new { m.UserId, m.Household!.Name })
            .ToListAsync(ct);
        var now = clock.GetUtcNow();
        return all.Select(u => new AdminUserDto(
            u.Id,
            u.Email ?? u.UserName ?? "",
            u.Id == MyId,
            admins.Contains(u.Id),
            u.TwoFactorEnabled,
            AccountBlocking.IsBlocked(u),
            !AccountBlocking.IsBlocked(u) && u.LockoutEnd > now ? u.LockoutEnd : null,
            memberships.Where(m => m.UserId == u.Id).Select(m => m.Name).ToList()));
    }

    [HttpPost("{id}/block")]
    public Task<IActionResult> Block(string id) => WithUser(id, async user =>
    {
        if (user.Id == MyId) return Problem(statusCode: 400, title: "Je kunt jezelf niet blokkeren.");
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, AccountBlocking.BlockedUntil);
        await users.UpdateSecurityStampAsync(user);
        return NoContent();
    });

    [HttpPost("{id}/unblock")]
    public Task<IActionResult> Unblock(string id) => WithUser(id, async user =>
    {
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return NoContent();
    });

    /// <summary>Schakelt 2FA uit en maakt de gekoppelde app ongeldig (telefoon én herstelcodes kwijt).</summary>
    [HttpPost("{id}/reset-2fa")]
    public Task<IActionResult> ResetTwoFactor(string id) => WithUser(id, async user =>
    {
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.UpdateSecurityStampAsync(user);
        return NoContent();
    });

    /// <summary>Herstellink om zelf te delen (werkt ook zonder mailserver); één dag geldig.</summary>
    [HttpPost("{id}/password-reset")]
    public Task<IActionResult> PasswordReset(string id) => WithUser(id, async user =>
        Ok(new ResetLinkDto(await PasswordResetLinks.CreatePathAsync(users, user))));

    [HttpPut("{id}/admin")]
    public Task<IActionResult> SetAdmin(string id, SetAdminRequest request) => WithUser(id, async user =>
    {
        var isAdmin = await users.IsInRoleAsync(user, AdminAuthorization.Role);
        if (request.IsAdmin && !isAdmin) await users.AddToRoleAsync(user, AdminAuthorization.Role);
        if (!request.IsAdmin && isAdmin)
        {
            if ((await users.GetUsersInRoleAsync(AdminAuthorization.Role)).Count <= 1)
                return Problem(statusCode: 409, title: "Er moet minstens één beheerder overblijven.");
            await users.RemoveFromRoleAsync(user, AdminAuthorization.Role);
        }
        return NoContent();
    });

    /// <summary>Wat er verdwijnt bij verwijderen: huishoudens waarvan dit account het enige lid is.</summary>
    [HttpGet("{id}/delete-preview")]
    public Task<IActionResult> DeletePreview(string id, CancellationToken ct) => WithUser(id, async user =>
        Ok(new DeletePreviewDto((await households.SoleHouseholdsAsync(user.Id, ct))
            .Select(h => new DeletedHouseholdDto(h.Household.Name, h.Items)).ToList())));

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct) => WithUser(id, async user =>
    {
        if (user.Id == MyId) return Problem(statusCode: 400, title: "Je kunt je eigen account hier niet verwijderen.");
        if (await users.IsInRoleAsync(user, AdminAuthorization.Role)
            && (await users.GetUsersInRoleAsync(AdminAuthorization.Role)).Count <= 1)
            return Problem(statusCode: 409, title: "Er moet minstens één beheerder overblijven.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var (household, _) in await households.SoleHouseholdsAsync(user.Id, ct))
            await households.DeleteHouseholdAsync(household.Id, ct);
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded) return Problem(statusCode: 500, title: "Verwijderen is mislukt.");
        await transaction.CommitAsync(ct);
        return NoContent();
    });

    private async Task<IActionResult> WithUser(string id, Func<IdentityUser, Task<IActionResult>> action) =>
        await users.FindByIdAsync(id) is { } user ? await action(user) : NotFound();
}
