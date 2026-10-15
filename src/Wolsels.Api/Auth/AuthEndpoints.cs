using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolsels.Api.Data;

namespace Wolsels.Api.Auth;

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    /// <summary>
    /// Endpoints van ASP.NET Core Identity die zonder e-mailserver geen zin hebben of niet gebruikt worden.
    /// </summary>
    private static readonly string[] DisabledPaths =
        ["/confirmEmail", "/resendConfirmationEmail", "/forgotPassword", "/resetPassword", "/refresh"];

    public sealed record AuthOptionsDto(bool AllowRegistration);

    public sealed record ChangePasswordRequest(string OldPassword, string NewPassword);

    public sealed record MeDto(string Email, bool TwoFactorEnabled, int RecoveryCodesLeft);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        // /register, /login, /manage/2fa en /manage/info van Identity.
        // Login: POST /api/auth/login?useCookies=true met { email, password, twoFactorCode?, twoFactorRecoveryCode? }.
        var identity = auth.MapGroup("");
        identity.MapIdentityApi<IdentityUser>();
        identity.RequireRateLimiting(RateLimitPolicy);
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
            if (!allow && await users.Users.AnyAsync())
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Registreren is uitgeschakeld. Vraag de beheerder om een account.");

            var result = await next(context);
            await ClaimUnownedDataAsync(services);
            return result;
        });

        auth.MapGet("/options", (IOptions<AuthOptions> options, UserManager<IdentityUser> users) =>
                new AuthOptionsDto(options.Value.AllowRegistration || !users.Users.Any()))
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
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicy);

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

    /// <summary>
    /// Gegevens van vóór de invoering van accounts hebben geen eigenaar; die gaan naar het eerste account.
    /// </summary>
    private static async Task ClaimUnownedDataAsync(IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        if (await users.Users.CountAsync() != 1) return;
        var ownerId = (await users.Users.SingleAsync()).Id;

        var db = services.GetRequiredService<WolselsDbContext>();
        await db.Wools.IgnoreQueryFilters().Where(e => e.OwnerId == "").ExecuteUpdateAsync(s => s.SetProperty(e => e.OwnerId, ownerId));
        await db.Blends.IgnoreQueryFilters().Where(e => e.OwnerId == "").ExecuteUpdateAsync(s => s.SetProperty(e => e.OwnerId, ownerId));
        await db.Locations.IgnoreQueryFilters().Where(e => e.OwnerId == "").ExecuteUpdateAsync(s => s.SetProperty(e => e.OwnerId, ownerId));
        await db.Animals.IgnoreQueryFilters().Where(e => e.OwnerId == "").ExecuteUpdateAsync(s => s.SetProperty(e => e.OwnerId, ownerId));
        await db.Harvests.IgnoreQueryFilters().Where(e => e.OwnerId == "").ExecuteUpdateAsync(s => s.SetProperty(e => e.OwnerId, ownerId));
    }
}
