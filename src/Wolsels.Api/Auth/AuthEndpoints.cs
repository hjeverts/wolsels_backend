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
    /// ASP.NET Core Identity endpoints that are not used. Forgot password has its own endpoints
    /// below, because Identity's only work for confirmed email addresses.
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

        // /register, /login, /manage/2fa and /manage/info from Identity.
        // Login: POST /api/auth/login?useCookies=true with { email, password, twoFactorCode?, twoFactorRecoveryCode? }.
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
            // With a valid invitation (?invite=...) signing up works even when it is otherwise disabled.
            var invite = context.HttpContext.Request.Query["invite"].ToString();
            if (!allow && await users.Users.AnyAsync()
                && await services.GetRequiredService<HouseholdService>().FindValidInviteAsync(invite, context.HttpContext.RequestAborted) is null)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: Texts.Get("Registration.Disabled"));

            var result = await next(context);
            // The very first account becomes the site administrator.
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

        // Forgot password: sends a reset link. Always gives the same response, so it cannot be found out
        // which email addresses have an account.
        auth.MapPost("/password/forgot", async (ForgotPasswordRequest request, HttpContext http,
                UserManager<IdentityUser> users, IMailSender mail, IOptions<AppOptions> app, IOptions<AuthOptions> authOptions,
                IMemoryCache cache, TimeProvider clock, ILoggerFactory loggers, WolselsDbContext db) =>
            {
                if (!PasswordResetAvailable(mail, app.Value)) return Results.NotFound();
                if (await users.FindByEmailAsync(request.Email.Trim()) is { Email: { } email } user
                    && TryCountMail(cache, clock, email, authOptions.Value.PasswordResetPerEmailPerHour))
                {
                    // Never use the request's Host header: it can be forged (password reset poisoning).
                    var link = app.Value.PublicUrl!.TrimEnd('/') + await PasswordResetLinks.CreatePathAsync(users, user);
                    // The mail in the profile language; without one, in the request language.
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
                        loggers.CreateLogger("Wolsels.Auth").LogError(ex, "Sending the password reset mail failed");
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

        // App language, stored in the profile so that it applies on every device.
        auth.MapPut("/language", async (LanguageRequest request, HttpContext http, UserManager<IdentityUser> users,
                WolselsDbContext db) =>
            {
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();
                if (request.Language is not { } code || !Texts.Languages.Contains(code))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["language"] = [Texts.Get("Language.Unsupported")],
                    });
                // Atomic upsert: two concurrent first saves (e.g. two tabs) must not both try to insert the row.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "UserSettings" ("UserId", "Language") VALUES ({user.Id}, {code})
                    ON CONFLICT ("UserId") DO UPDATE SET "Language" = excluded."Language"
                    """, http.RequestAborted);
                return Results.NoContent();
            })
            .RequireAuthorization();

        // Change the password and issue a new session cookie right away (the security stamp changes).
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

        // Renew the session cookie after changing 2FA settings, otherwise an unexpected logout follows later.
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

    /// <summary>Reset mails require a mail server and a fixed public URL for the link.</summary>
    private static bool PasswordResetAvailable(IMailSender mail, AppOptions app) =>
        mail.IsConfigured && Uri.TryCreate(app.PublicUrl, UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp);

    /// <summary>
    /// Counts reset mails per email address in a one-hour window. Above the limit nothing is sent, silently,
    /// so that nobody's mailbox can be flooded and the response gives nothing away.
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
