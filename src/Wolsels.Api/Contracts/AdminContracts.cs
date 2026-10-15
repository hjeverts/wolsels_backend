namespace Wolsels.Api.Contracts;

public sealed record AdminUserDto(
    string Id,
    string Email,
    bool IsYou,
    bool IsAdmin,
    bool TwoFactorEnabled,
    bool Blocked,
    DateTimeOffset? LockedOutUntil,
    IReadOnlyList<string> Households);

public sealed record DeletePreviewDto(IReadOnlyList<DeletedHouseholdDto> HouseholdsDeleted);

public sealed record DeletedHouseholdDto(string Name, int Items);

public sealed record ResetLinkDto(string Path);

public sealed class SetAdminRequest
{
    public bool IsAdmin { get; set; }
}
