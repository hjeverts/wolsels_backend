namespace Wolsels.Api.Domain;

/// <summary>
/// A household shares all data (wool, blends, animals, ...). Several logins (e.g. partners) can
/// form one household together, and a user can be a member of several households.
/// </summary>
public class Household
{
    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>User who created the household (for information).</summary>
    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class HouseholdMember
{
    public required string UserId { get; set; }
    public int HouseholdId { get; set; }
    public Household? Household { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

/// <summary>Invitation to join a household; single use.</summary>
public class HouseholdInvite
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    /// <summary>SHA-256 of the token; the token itself is not stored.</summary>
    public required string TokenHash { get; set; }
    public required string CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string? UsedByUserId { get; set; }
}

/// <summary>Data belonging to a household; other households never see it.</summary>
public interface IHouseholdData
{
    int HouseholdId { get; set; }
}

/// <summary>Personal settings of a user (independent of the household).</summary>
public class UserSettings
{
    public required string UserId { get; set; }
    /// <summary>Language code of the app, see <see cref="Localization.Texts.Languages"/>.</summary>
    public required string Language { get; set; }
}
