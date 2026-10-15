using System.ComponentModel.DataAnnotations;

namespace Wolsels.Api.Contracts;

public sealed record HouseholdMemberDto(string UserId, string Email, bool IsYou, DateTimeOffset JoinedAt);

public sealed record HouseholdInviteDto(int Id, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record HouseholdDto(
    int Id,
    string Name,
    IReadOnlyList<HouseholdMemberDto> Members,
    IReadOnlyList<HouseholdInviteDto> Invites);

public sealed record CreatedInviteDto(int Id, string Token, DateTimeOffset ExpiresAt);

public sealed record InviteInfoDto(string HouseholdName, string InvitedBy, DateTimeOffset ExpiresAt);

public sealed record HouseholdSummaryDto(int Id, string Name, int MemberCount);

public sealed record JoinedDto(int HouseholdId, string Name);

public sealed class HouseholdRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = "";
}

public sealed class JoinRequest
{
    [Required, MaxLength(100)] public string Token { get; set; } = "";
}
