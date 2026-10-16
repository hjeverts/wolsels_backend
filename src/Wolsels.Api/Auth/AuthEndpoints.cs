using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Wolsels.Api.Data;
using Wolsels.Api.Domain;
using Wolsels.Api.Households;
using Wolsels.Api.Mail;
using Wolsels.Api.Localization;

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

    public sealed record MeDto(string Email, bool TwoFactorEnabled, int RecoveryCodesLeft, bool IsAdmin, string? Language);

    public sealed record LanguageRequest(string? Language);

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
                    title: Texts.Get("Registration.Disabled"));

            var result = await next(context);
            // Het allereerste account wordt beheerder van de site.
            if (context.Arguments.OfType<Microsoft.AspNetCore.Identity.Data.RegisterRequest>().FirstOrDefault() is { } request
                && await users.FindByEmailAsync(request.Email) is { } created
                && (await users.GetUsersInRoleAsync(Admin.AdminAuthorization.Role)).Count == 0)
            {
                await users.AddToRoleAsync(created, Admin.AdminAuthorization.Role);
            }
            return result;
        });

        auth.MapGet("/options", (IOptions<AuthOptions> options, UserManager<IdentityUser> users, IMailSender mail,
                IOptions<AppOptions> app) =>
                new AuthOptionsDto(options.Value.AllowRegistration || !users.Users.Any(), PasswordResetAvailable(mail, app.Value)))
            .AllowAnonymous();

        // Wachtwoord vergeten: stuurt een herstellink. Antwoordt altijd hetzelfde, zodat niet te achterhalen
        // is welke e-mailadressen een account hebben.
        auth.MapPost("/password/forgot", async (ForgotPasswordRequest request, HttpContext http,
                UserManager<IdentityUser> users, IMailSender mail, IOptions<AppOptions> app, IOptions<AuthOptions> authOptions,
                IMemoryCache cache, TimeProvider clock, ILoggerFactory loggers, WolselsDbContext db) =>
            {
                if (!PasswordResetAvailable(mail, app.Value)) return Results.NotFound();
                if (await users.FindByEmailAsync(request.Email.Trim()) is { Email: { } email } user
                    && TryCountMail(cache, clock, email, authOptions.Value.PasswordResetPerEmailPerHour))
                {
                    // Nooit de Host-header van het verzoek gebruiken: die kan vervalst zijn (password reset poisoning).
                    var link = app.Value.PublicUrl!.TrimEnd('/') + await PasswordResetLinks.CreatePathAsync(users, user);
                    // De mail in de taal uit het profiel; zonder profieltaal die van het verzoek.
                    var language = (await db.UserSettings.FindAsync([user.Id], http.RequestAborted))?.Language ?? Texts.CurrentLanguage;
                    string T(string key) => Texts.GetFor(language, key);
                    string Html(string key) => WebUtility.HtmlEncode(T(key));
                    try
                    {
                        await mail.SendAsync(email, T("PasswordReset.MailSubject"),
                            $"{T("PasswordReset.MailGreeting")}\n\n{T("PasswordReset.MailIntro")} {T("PasswordReset.MailOpenLink")}\n\n{link}\n\n{T("PasswordReset.MailFooter")}",
                            $"<p>{Html("PasswordReset.MailGreeting")}</p><p>{Html("PasswordReset.MailIntro")}</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">{Html("PasswordReset.MailLinkText")}</a></p><p>{Html("PasswordReset.MailFooter")}</p>",
                            http.RequestAborted);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        loggers.CreateLogger("Wolsels.Auth").LogError(ex, "Versturen van de herstelmail is mislukt");
                        return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                            title: Texts.Get("PasswordReset.MailFailed"));
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
                    ["code"] = [Texts.Get("PasswordReset.InvalidLink")],
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

        auth.MapGet("/me", async (HttpContext http, UserManager<IdentityUser> users, WolselsDbContext db) =>
            {
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();
                return Results.Ok(new MeDto(
                    user.Email ?? user.UserName ?? "",
                    await users.GetTwoFactorEnabledAsync(user),
                    await users.CountRecoveryCodesAsync(user),
                    await users.IsInRoleAsync(user, Admin.AdminAuthorization.Role),
                    (await db.UserSettings.FindAsync([user.Id], http.RequestAborted))?.Language));
            })
            .RequireAuthorization();

        // Taal van de app, opgeslagen in het profiel zodat die op elk apparaat geldt.
        auth.MapPut("/language", async (LanguageRequest request, HttpContext http, UserManager<IdentityUser> users,
                WolselsDbContext db) =>
            {
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();
                if (request.Language is not { } code || !Texts.Languages.Contains(code))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["language"] = [Texts.Get("Language.Unsupported")],
                    });
                if (await db.UserSettings.FindAsync([user.Id], http.RequestAborted) is { } settings)
                    settings.Language = code;
                else
                    db.UserSettings.Add(new UserSettings { UserId = user.Id, Language = code });
                await db.SaveChangesAsync(http.RequestAborted);
                return Results.NoContent();
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
