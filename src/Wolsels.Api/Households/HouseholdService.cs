using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;

namespace Wolsels.Api.Households;

public class HouseholdException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record InviteInfo(HouseholdInvite Invite, Household Household, string InvitedBy);

/// <summary>Beheer van huishoudens: lidmaatschap, uitnodigingen, toetreden en verlaten.</summary>
public class HouseholdService(WolselsDbContext db, UserManager<IdentityUser> users, TimeProvider clock)
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);
    public const int MaxOpenInvites = 10;

    /// <summary>
    /// Bepaalt het actieve huishouden: het gevraagde (als de gebruiker er lid van is), anders het eerste
    /// huishouden van de gebruiker. Een gebruiker zonder huishouden krijgt er een.
    /// </summary>
    /// <returns>null als het gevraagde huishouden niet van deze gebruiker is.</returns>
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
            name = $"Huishouden van {user?.Email ?? "onbekend"}";
        }
        var household = new Household { Name = name.Trim(), CreatedByUserId = userId, CreatedAt = now };
        db.Households.Add(household);
        db.HouseholdMembers.Add(new HouseholdMember { UserId = userId, Household = household, JoinedAt = now });
        await db.SaveChangesAsync(ct);

        // Gegevens van vóór de invoering van accounts (zonder huishouden) gaan naar het allereerste huishouden.
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
                $"Er staan al {MaxOpenInvites} uitnodigingen open. Trek er eerst een in.");

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

    /// <summary>Zoekt een geldige (niet verlopen, niet gebruikte) uitnodiging op.</summary>
    public async Task<InviteInfo?> FindValidInviteAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = Hash(token);
        var now = clock.GetUtcNow();
        var invite = await db.HouseholdInvites.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invite is null || invite.UsedAt is not null || invite.ExpiresAt <= now) return null;
        var household = await db.Households.FirstAsync(h => h.Id == invite.HouseholdId, ct);
        var inviter = await users.FindByIdAsync(invite.CreatedByUserId);
        return new InviteInfo(invite, household, inviter?.Email ?? "een lid");
    }

    /// <summary>Voegt de gebruiker toe aan het huishouden van de uitnodiging (naast zijn andere huishoudens).</summary>
    public async Task<Household> JoinAsync(string userId, string token, CancellationToken ct)
    {
        var info = await FindValidInviteAsync(token, ct)
            ?? throw new HouseholdException(StatusCodes.Status400BadRequest, "Deze uitnodiging is ongeldig, verlopen of al gebruikt.");
        if (await IsMemberAsync(userId, info.Household.Id, ct))
            throw new HouseholdException(StatusCodes.Status409Conflict, "Je bent al lid van dit huishouden.");

        var now = clock.GetUtcNow();
        db.HouseholdMembers.Add(new HouseholdMember { UserId = userId, HouseholdId = info.Household.Id, JoinedAt = now });
        info.Invite.UsedAt = now;
        info.Invite.UsedByUserId = userId;
        await db.SaveChangesAsync(ct);
        return info.Household;
    }

    /// <summary>
    /// De gebruiker verlaat een huishouden. Het laatste lid kan niet vertrekken, zodat er nooit gegevens zonder
    /// eigenaar achterblijven. Wie geen huishouden meer heeft, krijgt een nieuw, leeg huishouden.
    /// </summary>
    public async Task LeaveAsync(string userId, int householdId, CancellationToken ct)
    {
        await RemoveMembershipAsync(householdId, userId,
            "Je bent het laatste lid van dit huishouden; verlaten kan pas als er nog iemand anders lid is.", ct);
    }

    /// <summary>Verwijdert een ander lid uit het huishouden.</summary>
    public async Task RemoveMemberAsync(int householdId, string userId, string memberUserId, CancellationToken ct)
    {
        if (memberUserId == userId)
            throw new HouseholdException(StatusCodes.Status400BadRequest, "Gebruik 'Huishouden verlaten' om jezelf te verwijderen.");
        if (!await IsMemberAsync(memberUserId, householdId, ct))
            throw new HouseholdException(StatusCodes.Status404NotFound, "Dit lid hoort niet bij dit huishouden.");
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

    /// <summary>Verwijdert een huishouden met al zijn gegevens (bij het verwijderen van het laatste lid).</summary>
    public async Task DeleteHouseholdAsync(int householdId, CancellationToken ct)
    {
        // Volgorde i.v.m. verwijzingen: oogsten -> mengsels (componenten via cascade) -> dieren -> locaties -> wol.
        await db.Harvests.IgnoreQueryFilters().Where(e => e.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        await db.Blends.IgnoreQueryFilters().Where(e => e.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        await db.Animals.IgnoreQueryFilters().Where(e => e.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        await db.Locations.IgnoreQueryFilters().Where(e => e.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        await db.Wools.IgnoreQueryFilters().Where(e => e.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        await db.Households.Where(h => h.Id == householdId).ExecuteDeleteAsync(ct);
    }

    /// <summary>Huishoudens waarvan de gebruiker het enige lid is, met het aantal gegevens erin.</summary>
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
