using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Wolsels.Api.Auth;

public static class PasswordResetLinks
{
    /// <summary>Relatief pad naar de pagina "nieuw wachtwoord", met een eenmalige code (één dag geldig).</summary>
    public static async Task<string> CreatePathAsync(UserManager<IdentityUser> users, IdentityUser user)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return $"/wachtwoord-herstellen?email={Uri.EscapeDataString(user.Email ?? "")}&code={code}";
    }
}
