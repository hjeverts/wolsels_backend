using Microsoft.AspNetCore.Identity;
using Wolsels.Api.Admin;
using Wolsels.Api.Services;
using Wolsels.Api.Localization;

namespace Wolsels.Api.Households;

/// <summary>
/// Determines the active household per request. The frontend sends, per tab, which household it shows
/// (header <c>X-Household</c>); the server checks on every request that the user is a member of it.
/// Deliberately not in the session cookie: whoever is removed from a household loses access immediately, and
/// a tab showing another household can never accidentally save into the wrong household.
/// </summary>
public class HouseholdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Household";
    public const string QueryName = "household";

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, HouseholdService households,
        UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn)
    {
        if (currentUser is HttpCurrentUser user && user.UserId is { } userId)
        {
            // Deleted accounts, or accounts blocked by an administrator, lose their session immediately.
            var account = await users.FindByIdAsync(userId);
            if (account is null || AccountBlocking.IsBlocked(account))
            {
                await signIn.SignOutAsync();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { status = 401, title = Texts.Get("Auth.AccountBlocked") });
                return;
            }

            // Account matters and the list of one's own households don't depend on the active household.
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api/auth"))
            {
                await next(context);
                return;
            }

            int? requested = null;
            if (!path.StartsWithSegments("/api/households") && TryGetRequested(context.Request, out var header))
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

    /// <summary>
    /// The requested household from the header, or for GET from <c>?household=</c>: an &lt;img&gt; cannot send a
    /// header. Read-only, and membership is checked the same way.
    /// </summary>
    private static bool TryGetRequested(HttpRequest request, out string? value)
    {
        if (request.Headers.TryGetValue(HeaderName, out var header))
        {
            value = header;
            return true;
        }
        if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) && request.Query.TryGetValue(QueryName, out var query))
        {
            value = query;
            return true;
        }
        value = null;
        return false;
    }

    private static Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new
        {
            type = "household-access",
            status = 403,
            title = Texts.Get("Household.NotMember"),
        });
    }
}
