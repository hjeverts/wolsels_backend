using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Wolsels.Api.Households;
using Wolsels.Api.Mail;

namespace Wolsels.Api.Auth;

public static class AuthEndpoints
{
    /// <summary>
    /// Endpoints van ASP.NET Core Identity die niet gebruikt worden. Wachtwoord vergeten heeft eigen endpoints
    /// hieronder, omdat die van Identity alleen werken voor bevestigde e-mailadressen.
    /// </summary>
    private static readonly string[] DisabledPaths =
        ["/confirmEmail", "/resendConfirmationEmail", "/forgotPassword", "/resetPassword", "/refresh"];

    public sealed record AuthOptionsDto(bool AllowRegistration, bool PasswordResetAvailable);

    public sealed record ForgotPasswordRequest(string Email);

    public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);

    public sealed record ChangePasswordRequest(string OldPassword, string NewPassword);

    public sealed record MeDto(string Email, bool TwoFactorEnabled, int RecoveryCodesLeft);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        // /register, /login, /manage/2fa en /manage/info van Identity.
        // Login: POST /api/auth/login?useCookies=true met { email, password, twoFactorCode?, twoFactorRecoveryCode? }.
        var identity = auth.MapGroup("");
        identity.MapIdentityApi<IdentityUser>();
        identity.AddEndpointFilter(async (context, next) =>
        {
            var path = context.HttpContext.Request.Path.Value ?? "";
            if (DisabledPaths.Any(p => path.EndsWith(p, StringComparison.OrdinalIgnoreCase)))
                return Results.NotFound();

            if (!path.EndsWith("/register", StringComparison.OrdinalIgnoreCase))
                return await next(context);

            var services = context.HttpContext.RequestServices;
            var users = services.GetRequiredService<UserManager<IdentityUser>>();
            var allow = services.GetRequiredService<IOptions<AuthOptions>>().Value.AllowRegistration;
            // Met een geldige uitnodiging (?invite=...) kan registreren ook als het verder uit staat.
            var invite = context.HttpContext.Request.Query["invite"].ToString();
            if (!allow && await users.Users.AnyAsync()
                && await services.GetRequiredService<HouseholdService>().FindValidInviteAsync(invite, context.HttpContext.RequestAborted) is null)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Registreren is uitgeschakeld. Vraag een lid om een uitnodiging.");

            return await next(context);
        });

        auth.MapGet("/options", (IOptions<AuthOptions> options, UserManager<IdentityUser> users, IMailSender mail,
                IOptions<AppOptions> app) =>
                new AuthOptionsDto(options.Value.AllowRegistration || !users.Users.Any(), PasswordResetAvailable(mail, app.Value)))
            .AllowAnonymous();

        // Wachtwoord vergeten: stuurt een herstellink. Antwoordt altijd hetzelfde, zodat niet te achterhalen
        // is welke e-mailadressen een account hebben.
        auth.MapPost("/password/forgot", async (ForgotPasswordRequest request, HttpContext http,
                UserManager<IdentityUser> users, IMailSender mail, IOptions<AppOptions> app, IOptions<AuthOptions> authOptions,
                IMemoryCache cache, TimeProvider clock, ILoggerFactory loggers) =>
            {
                if (!PasswordResetAvailable(mail, app.Value)) return Results.NotFound();
                if (await users.FindByEmailAsync(request.Email.Trim()) is { Email: { } email } user
                    && TryCountMail(cache, clock, email, authOptions.Value.PasswordResetPerEmailPerHour))
                {
                    var token = await users.GeneratePasswordResetTokenAsync(user);
                    var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                    // Nooit de Host-header van het verzoek gebruiken: die kan vervalst zijn (password reset poisoning).
                    var baseUrl = app.Value.PublicUrl!.TrimEnd('/');
                    var link = $"{baseUrl}/wachtwoord-herstellen?email={Uri.EscapeDataString(email)}&code={code}";
                    try
                    {
                        await mail.SendAsync(email, "Wachtwoord herstellen voor Wolsels",
                            $"Hallo,\n\nJe hebt gevraagd om je wachtwoord voor Wolsels te herstellen. Open deze link om een nieuw wachtwoord in te stellen:\n\n{link}\n\nDe link is één dag geldig. Heb je dit niet zelf gevraagd? Dan kun je deze mail negeren.",
                            $"<p>Hallo,</p><p>Je hebt gevraagd om je wachtwoord voor Wolsels te herstellen.</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Stel een nieuw wachtwoord in</a></p><p>De link is één dag geldig. Heb je dit niet zelf gevraagd? Dan kun je deze mail negeren.</p>",
                            http.RequestAborted);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        loggers.CreateLogger("Wolsels.Auth").LogError(ex, "Versturen van de herstelmail is mislukt");
                        return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                            title: "De e-mail kon niet verstuurd worden. Probeer het later opnieuw.");
                    }
                }
                return Results.NoContent();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.PasswordResetPolicy);

        auth.MapPost("/password/reset", async (ResetPasswordRequest request, UserManager<IdentityUser> users) =>
            {
                var invalid = Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["code"] = ["Deze herstellink is ongeldig of verlopen. Vraag een nieuwe aan."],
                });
                if (await users.FindByEmailAsync(request.Email.Trim()) is not { } user) return invalid;

                string token;
                try
                {
                    token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Code));
                }
                catch (FormatException)
                {
                    return invalid;
                }

                var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
                if (result.Succeeded)
                {
                    await users.ResetAccessFailedCountAsync(user);
                    await users.SetLockoutEndDateAsync(user, null);
                    return Results.NoContent();
                }
                if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))) return invalid;
                return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
            })
            .AllowAnonymous();

        auth.MapGet("/me", async (HttpContext http, UserManager<IdentityUser> users) =>
            {
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();
                return Results.Ok(new MeDto(
                    user.Email ?? user.UserName ?? "",
                    await users.GetTwoFactorEnabledAsync(user),
                    await users.CountRecoveryCodesAsync(user)));
            })
            .RequireAuthorization();

        // Wachtwoord wijzigen en direct een nieuwe sessiecookie uitgeven (de security stamp verandert).
        auth.MapPost("/password", async (ChangePasswordRequest request, HttpContext http,
                UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
            {
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();
                var result = await users.ChangePasswordAsync(user, request.OldPassword, request.NewPassword);
                if (!result.Succeeded)
                    return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
                await signIn.RefreshSignInAsync(user);
                return Results.NoContent();
            })
            .RequireAuthorization();

        // Na het wijzigen van 2FA-instellingen de sessiecookie vernieuwen, anders volgt later een onverwachte uitlog.
        auth.MapPost("/renew", async (HttpContext http, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
            {
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();
                await signIn.RefreshSignInAsync(user);
                return Results.NoContent();
            })
            .RequireAuthorization();

        auth.MapPost("/logout", async (SignInManager<IdentityUser> signIn) =>
            {
                await signIn.SignOutAsync();
                return Results.NoContent();
            })
            .RequireAuthorization();
    }

    /// <summary>Herstelmails vereisen een mailserver én een vaste openbare URL voor de link.</summary>
    private static bool PasswordResetAvailable(IMailSender mail, AppOptions app) =>
        mail.IsConfigured && Uri.TryCreate(app.PublicUrl, UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp);

    /// <summary>
    /// Telt herstelmails per e-mailadres in een venster van een uur. Boven de limiet wordt stil niets verstuurd,
    /// zodat iemands mailbox niet bestookt kan worden en het antwoord niets verraadt.
    /// </summary>
    private static bool TryCountMail(IMemoryCache cache, TimeProvider clock, string email, int limit)
    {
        var key = $"password-reset:{email.ToUpperInvariant()}";
        var now = clock.GetUtcNow();
        lock (cache)
        {
            var sent = (cache.Get<List<DateTimeOffset>>(key) ?? []).Where(t => t > now.AddHours(-1)).ToList();
            if (sent.Count >= limit) return false;
            sent.Add(now);
            cache.Set(key, sent, TimeSpan.FromHours(1));
            return true;
        }
    }
}
