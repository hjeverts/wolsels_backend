using Microsoft.AspNetCore.Identity;

namespace Wolsels.Api.Admin;

/// <summary>
/// Blokkeren door een beheerder gebruikt de lockout van Identity met een einddatum ver in de toekomst. Zo kan
/// een geblokkeerd account niet inloggen, en is het te onderscheiden van een tijdelijke lockout na foute pogingen.
/// </summary>
public static class AccountBlocking
{
    public static readonly DateTimeOffset BlockedUntil = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static bool IsBlocked(IdentityUser user) => user.LockoutEnd >= BlockedUntil;
}
