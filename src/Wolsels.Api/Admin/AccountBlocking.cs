using Microsoft.AspNetCore.Identity;

namespace Wolsels.Api.Admin;

/// <summary>
/// Blocking by an administrator uses Identity's lockout with an end date far in the future. That way
/// a blocked account cannot log in, and it can be told apart from a temporary lockout after failed attempts.
/// </summary>
public static class AccountBlocking
{
    public static readonly DateTimeOffset BlockedUntil = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static bool IsBlocked(IdentityUser user) => user.LockoutEnd >= BlockedUntil;
}
