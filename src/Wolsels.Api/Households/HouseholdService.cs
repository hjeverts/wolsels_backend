using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;
using Wolsels.Api.Localization;

namespace Wolsels.Api.Households;

public class HouseholdException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record InviteInfo(HouseholdInvite Invite, Household Household, string InvitedBy);

/// <summary>Household management: membership, invitations, joining and leaving.</summary>
public class HouseholdService(WolselsDbContext db, UserManager<IdentityUser> users, TimeProvider clock)
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);
    public const int MaxOpenInvites = 10;

    /// <summary>
    /// Determines the active household: the requested one (if the user is a member), otherwise the user's
    /// first household. A user without a household gets one.
    /// </summary>
    /// <returns>null if the requested household does not belong to this user.</returns>
    public async Task<int?> ResolveHouseholdAsync(string userId, int? requestedHouseholdId, CancellationToken ct = default)
    {
        if (requestedHouseholdId is { } requested)
            return await IsMemberAsync(userId, requested, ct) ? requested : null;

        var first = await db.HouseholdMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAt)
            .Select(m => (int?)m.HouseholdId)
            .FirstOrDefaultAsync(ct);
        return first ?? await CreateHouseholdAsync(userId, null, ct);
    }

    public Task<bool> IsMemberAsync(string userId, int householdId, CancellationToken ct = default) =>
        db.HouseholdMembers.AnyAsync(m => m.UserId == userId && m.HouseholdId == householdId, ct);

    public async Task<int> CreateHouseholdAsync(string userId, string? name, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        if (string.IsNullOrWhiteSpace(name))
        {
            var user = await users.FindByIdAsync(userId);
            name = Texts.Get("Household.DefaultName", user?.Email ?? Texts.Get("Household.UnknownUser"));
        }
        var household = new Household { Name = name.Trim(), CreatedByUserId = userId, CreatedAt = now };
        db.Households.Add(household);
        db.HouseholdMembers.Add(new HouseholdMember { UserId = userId, Household = household, JoinedAt = now });
        await db.SaveChangesAsync(ct);

        // Data from before accounts were introduced (without a household) goes to the very first household.
        if (await db.Households.CountAsync(ct) == 1)
        {
            await db.Wools.IgnoreQueryFilters().Where(e => e.HouseholdId == 0).ExecuteUpdateAsync(s => s.SetProperty(e => e.HouseholdId, household.Id), ct);
            await db.Blends.IgnoreQueryFilters().Where(e => e.HouseholdId == 0).ExecuteUpdateAsync(s => s.SetProperty(e => e.HouseholdId, household.Id), ct);
            await db.Locations.IgnoreQueryFilters().Where(e => e.HouseholdId == 0).ExecuteUpdateAsync(s => s.SetProperty(e => e.HouseholdId, household.Id), ct);
            await db.Animals.IgnoreQueryFilters().Where(e => e.HouseholdId == 0).ExecuteUpdateAsync(s => s.SetProperty(e => e.HouseholdId, household.Id), ct);
            await db.Harvests.IgnoreQueryFilters().Where(e => e.HouseholdId == 0).ExecuteUpdateAsync(s => s.SetProperty(e => e.HouseholdId, household.Id), ct);
        }
        return household.Id;
    }

    public async Task<(string Token, HouseholdInvite Invite)> CreateInviteAsync(int householdId, string userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var open = await db.HouseholdInvites.CountAsync(i => i.HouseholdId == householdId && i.UsedAt == null && i.ExpiresAt > now, ct);
        if (open >= MaxOpenInvites)
            throw new HouseholdException(StatusCodes.Status409Conflict,
                Texts.Get("Household.TooManyInvites", MaxOpenInvites));

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var invite = new HouseholdInvite
        {
            HouseholdId = householdId,
            TokenHash = Hash(token),
            CreatedByUserId = userId,
            CreatedAt = now,
            ExpiresAt = now + InviteLifetime,
        };
        db.HouseholdInvites.Add(invite);
        await db.SaveChangesAsync(ct);
        return (token, invite);
    }

    /// <summary>Looks up a valid (not expired, not used) invitation.</summary>
    public async Task<InviteInfo?> FindValidInviteAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = Hash(token);
        var now = clock.GetUtcNow();
        var invite = await db.HouseholdInvites.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invite is null || invite.UsedAt is not null || invite.ExpiresAt <= now) return null;
        var household = await db.Households.FirstAsync(h => h.Id == invite.HouseholdId, ct);
        var inviter = await users.FindByIdAsync(invite.CreatedByUserId);
        return new InviteInfo(invite, household, inviter?.Email ?? Texts.Get("Household.SomeMember"));
    }

    /// <summary>Adds the user to the household of the invitation (in addition to their other households).</summary>
    public async Task<Household> JoinAsync(string userId, string token, CancellationToken ct)
    {
        var info = await FindValidInviteAsync(token, ct)
            ?? throw new HouseholdException(StatusCodes.Status400BadRequest, Texts.Get("Household.InviteInvalid"));
        if (await IsMemberAsync(userId, info.Household.Id, ct))
            throw new HouseholdException(StatusCodes.Status409Conflict, Texts.Get("Household.AlreadyMember"));

        var now = clock.GetUtcNow();
        db.HouseholdMembers.Add(new HouseholdMember { UserId = userId, HouseholdId = info.Household.Id, JoinedAt = now });
        info.Invite.UsedAt = now;
        info.Invite.UsedByUserId = userId;
        await db.SaveChangesAsync(ct);
        return info.Household;
    }

    /// <summary>
    /// The user leaves a household. The last member cannot leave, so that data is never left without
    /// an owner. Whoever has no household left gets a new, empty household.
    /// </summary>
    public async Task LeaveAsync(string userId, int householdId, CancellationToken ct)
    {
        await RemoveMembershipAsync(householdId, userId,
            Texts.Get("Household.LastMember"), ct);
    }

    /// <summary>Removes another member from the household.</summary>
    public async Task RemoveMemberAsync(int householdId, string userId, string memberUserId, CancellationToken ct)
    {
        if (memberUserId == userId)
            throw new HouseholdException(StatusCodes.Status400BadRequest, Texts.Get("Household.UseLeave"));
        if (!await IsMemberAsync(memberUserId, householdId, ct))
            throw new HouseholdException(StatusCodes.Status404NotFound, Texts.Get("Household.MemberNotFound"));
        await RemoveMembershipAsync(householdId, memberUserId, "", ct);
    }

    private async Task RemoveMembershipAsync(int householdId, string userId, string lastMemberMessage, CancellationToken ct)
    {
        if (await db.HouseholdMembers.CountAsync(m => m.HouseholdId == householdId, ct) < 2)
            throw new HouseholdException(StatusCodes.Status409Conflict, lastMemberMessage);

        await db.HouseholdMembers.Where(m => m.UserId == userId && m.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        if (!await db.HouseholdMembers.AnyAsync(m => m.UserId == userId, ct))
            await CreateHouseholdAsync(userId, null, ct);
    }

    /// <summary>Deletes a household with all its data (when deleting the last member).</summary>
    public async Task DeleteHouseholdAsync(int householdId, CancellationToken ct)
    {
        // Order because of references: harvests -> blends (components via cascade) -> animals -> locations -> wool
        // (photos via cascade).
        await db.Harvests.IgnoreQueryFilters().Where(e => e.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        await db.Blends.IgnoreQueryFilters().Where(e => e.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        await db.Animals.IgnoreQueryFilters().Where(e => e.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        await db.Locations.IgnoreQueryFilters().Where(e => e.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        await db.Wools.IgnoreQueryFilters().Where(e => e.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        await db.Households.Where(h => h.Id == householdId).ExecuteDeleteAsync(ct);
    }

    /// <summary>Households of which the user is the only member, with the number of items in them.</summary>
    public async Task<List<(Household Household, int Items)>> SoleHouseholdsAsync(string userId, CancellationToken ct)
    {
        var ids = await db.HouseholdMembers.Where(m => m.UserId == userId)
            .Where(m => !db.HouseholdMembers.Any(o => o.HouseholdId == m.HouseholdId && o.UserId != userId))
            .Select(m => m.HouseholdId).ToListAsync(ct);
        var result = new List<(Household, int)>();
        foreach (var id in ids)
        {
            var items = await db.Wools.IgnoreQueryFilters().CountAsync(e => e.HouseholdId == id, ct)
                + await db.Blends.IgnoreQueryFilters().CountAsync(e => e.HouseholdId == id, ct)
                + await db.Animals.IgnoreQueryFilters().CountAsync(e => e.HouseholdId == id, ct)
                + await db.Locations.IgnoreQueryFilters().CountAsync(e => e.HouseholdId == id, ct)
                + await db.Harvests.IgnoreQueryFilters().CountAsync(e => e.HouseholdId == id, ct);
            result.Add((await db.Households.FirstAsync(h => h.Id == id, ct), items));
        }
        return result;
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
