namespace Wolsels.Api.Domain;

/// <summary>
/// Een huishouden deelt alle gegevens (wol, mengsels, dieren, ...). Meerdere logins (bv. partners) kunnen
/// samen één huishouden vormen, en een gebruiker kan lid zijn van meerdere huishoudens.
/// </summary>
public class Household
{
    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Gebruiker die het huishouden heeft aangemaakt (ter informatie).</summary>
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

/// <summary>Uitnodiging om lid te worden van een huishouden; eenmalig te gebruiken.</summary>
public class HouseholdInvite
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    /// <summary>SHA-256 van de token; de token zelf wordt niet opgeslagen.</summary>
    public required string TokenHash { get; set; }
    public required string CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string? UsedByUserId { get; set; }
}

/// <summary>Gegevens die bij een huishouden horen; andere huishoudens zien ze nooit.</summary>
public interface IHouseholdData
{
    int HouseholdId { get; set; }
}
