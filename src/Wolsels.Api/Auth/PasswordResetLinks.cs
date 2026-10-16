using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Wolsels.Api.Auth;

public static class PasswordResetLinks
{
    /// <summary>Relative path to the "new password" page, with a single-use code (valid for one day).</summary>
    public static async Task<string> CreatePathAsync(UserManager<IdentityUser> users, IdentityUser user)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return $"/wachtwoord-herstellen?email={Uri.EscapeDataString(user.Email ?? "")}&code={code}";
    }
}
