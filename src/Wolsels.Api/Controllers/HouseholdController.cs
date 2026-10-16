using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Contracts;
using Wolsels.Api.Data;
using Wolsels.Api.Households;
using Wolsels.Api.Services;
using Wolsels.Api.Localization;

namespace Wolsels.Api.Controllers;

[ApiController]
[Route("api/household")]
public class HouseholdController(
    WolselsDbContext db, HouseholdService households, ICurrentUser currentUser,
    UserManager<IdentityUser> users, TimeProvider clock) : ControllerBase
{
    private string UserId => currentUser.UserId!;
    private int HouseholdId => currentUser.HouseholdId!.Value;

    [HttpGet]
    public async Task<HouseholdDto> Get(CancellationToken ct)
    {
        var household = await db.Households.AsNoTracking().FirstAsync(h => h.Id == HouseholdId, ct);
        var members = await db.HouseholdMembers.AsNoTracking().Where(m => m.HouseholdId == HouseholdId)
            .Join(users.Users, m => m.UserId, u => u.Id, (m, u) => new { m.UserId, u.Email, m.JoinedAt })
            .ToListAsync(ct);
        var now = clock.GetUtcNow();
        var invites = await db.HouseholdInvites.AsNoTracking()
            .Where(i => i.HouseholdId == HouseholdId && i.UsedAt == null && i.ExpiresAt > now)
            .Join(users.Users, i => i.CreatedByUserId, u => u.Id, (i, u) => new { i.Id, u.Email, i.CreatedAt, i.ExpiresAt })
            .ToListAsync(ct);

        return new HouseholdDto(
            household.Id,
            household.Name,
            members.OrderBy(m => m.JoinedAt)
                .Select(m => new HouseholdMemberDto(m.UserId, m.Email ?? "", m.UserId == UserId, m.JoinedAt)).ToList(),
            invites.OrderBy(i => i.CreatedAt)
                .Select(i => new HouseholdInviteDto(i.Id, i.Email ?? "", i.CreatedAt, i.ExpiresAt)).ToList());
    }

    [HttpPut]
    public async Task<IActionResult> Rename(HouseholdRequest request, CancellationToken ct)
    {
        var household = await db.Households.FirstAsync(h => h.Id == HouseholdId, ct);
        household.Name = request.Name.Trim();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Maakt een eenmalige uitnodigingslink; de token wordt alleen nu getoond.</summary>
    [HttpPost("invites")]
    public Task<ActionResult<CreatedInviteDto>> CreateInvite(CancellationToken ct) => Handle(async () =>
    {
        var (token, invite) = await households.CreateInviteAsync(HouseholdId, UserId, ct);
        return new CreatedInviteDto(invite.Id, token, invite.ExpiresAt);
    });

    [HttpDelete("invites/{id:int}")]
    public async Task<IActionResult> RevokeInvite(int id, CancellationToken ct)
    {
        var deleted = await db.HouseholdInvites.Where(i => i.Id == id && i.HouseholdId == HouseholdId && i.UsedAt == null)
            .ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound() : NoContent();
    }

    /// <summary>Gegevens van een uitnodiging, voor de uitnodigingspagina (ook zonder login).</summary>
    [AllowAnonymous]
    [HttpGet("invites/{token}")]
    public async Task<ActionResult<InviteInfoDto>> GetInvite(string token, CancellationToken ct) =>
        await households.FindValidInviteAsync(token, ct) is { } info
            ? new InviteInfoDto(info.Household.Name, info.InvitedBy, info.Invite.ExpiresAt)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: Texts.Get("Household.InviteInvalid"));

    /// <summary>Wordt lid van het huishouden van de uitnodiging (naast je andere huishoudens).</summary>
    [HttpPost("join")]
    public Task<ActionResult<JoinedDto>> Join(JoinRequest request, CancellationToken ct) => Handle(async () =>
    {
        var household = await households.JoinAsync(UserId, request.Token, ct);
        return new JoinedDto(household.Id, household.Name);
    });

    /// <summary>Verlaat het actieve huishouden.</summary>
    [HttpPost("leave")]
    public Task<ActionResult<object>> Leave(CancellationToken ct) => Handle<object>(async () =>
    {
        await households.LeaveAsync(UserId, HouseholdId, ct);
        return new { };
    });

    [HttpDelete("members/{userId}")]
    public Task<ActionResult<object>> RemoveMember(string userId, CancellationToken ct) => Handle<object>(async () =>
    {
        await households.RemoveMemberAsync(HouseholdId, UserId, userId, ct);
        return new { };
    });

    private async Task<ActionResult<T>> Handle<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (HouseholdException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }
}

/// <summary>De huishoudens van de ingelogde gebruiker, los van het actieve huishouden.</summary>
[ApiController]
[Route("api/households")]
public class HouseholdsController(WolselsDbContext db, HouseholdService households, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<HouseholdSummaryDto>> List(CancellationToken ct)
    {
        var userId = currentUser.UserId!;
        return await db.HouseholdMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new HouseholdSummaryDto(
                m.HouseholdId,
                m.Household!.Name,
                db.HouseholdMembers.Count(o => o.HouseholdId == m.HouseholdId)))
            .ToListAsync(ct);
    }

    /// <summary>Maakt een extra (leeg) huishouden aan met de gebruiker als enig lid.</summary>
    [HttpPost]
    public async Task<ActionResult<HouseholdSummaryDto>> Create(HouseholdRequest request, CancellationToken ct)
    {
        var id = await households.CreateHouseholdAsync(currentUser.UserId!, request.Name, ct);
        return new HouseholdSummaryDto(id, request.Name.Trim(), 1);
    }
}
