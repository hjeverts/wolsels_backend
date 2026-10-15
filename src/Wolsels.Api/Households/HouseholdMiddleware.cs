using Microsoft.AspNetCore.Identity;
using Wolsels.Api.Admin;
using Wolsels.Api.Services;

namespace Wolsels.Api.Households;

/// <summary>
/// Bepaalt per verzoek het actieve huishouden. De frontend stuurt per tabblad mee welk huishouden hij toont
/// (header <c>X-Household</c>); de server controleert bij elk verzoek of de gebruiker daar lid van is.
/// Bewust niet in de sessiecookie: wie uit een huishouden wordt verwijderd, verliest direct de toegang, en
/// een tabblad met een ander huishouden kan nooit per ongeluk in het verkeerde huishouden opslaan.
/// </summary>
public class HouseholdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Household";

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, HouseholdService households,
        UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn)
    {
        if (currentUser is HttpCurrentUser user && user.UserId is { } userId)
        {
            // Verwijderde of door een beheerder geblokkeerde accounts verliezen direct hun sessie.
            var account = await users.FindByIdAsync(userId);
            if (account is null || AccountBlocking.IsBlocked(account))
            {
                await signIn.SignOutAsync();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { status = 401, title = "Je account is geblokkeerd of bestaat niet meer." });
                return;
            }

            int? requested = null;
            if (context.Request.Headers.TryGetValue(HeaderName, out var header))
            {
                if (!int.TryParse(header, out var id))
                {
                    await Reject(context);
                    return;
                }
                requested = id;
            }

            user.HouseholdId = await households.ResolveHouseholdAsync(userId, requested, context.RequestAborted);
            if (user.HouseholdId is null)
            {
                await Reject(context);
                return;
            }
        }
        await next(context);
    }

    private static Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new
        {
            type = "household-access",
            status = 403,
            title = "Je bent geen lid (meer) van dit huishouden.",
        });
    }
}
