using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Wolsels.Api.Admin;

public static class AdminAuthorization
{
    public const string Role = "Admin";
    public const string Policy = "Admin";

    public static IServiceCollection AddAdminAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, AdminRequirementHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, p => p.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));
        return services;
    }

    /// <summary>
    /// Makes sure the administrator role exists and that there is at least one administrator: in an existing
    /// installation the creator of the first household becomes administrator. In addition Auth:AdminEmail (if set)
    /// always becomes administrator, so that access can be restored.
    /// </summary>
    public static async Task EnsureAdminsAsync(IServiceProvider services, string? adminEmail, ILogger logger)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        if (!await roles.RoleExistsAsync(Role)) await roles.CreateAsync(new IdentityRole(Role));

        if (!string.IsNullOrWhiteSpace(adminEmail))
        {
            if (await users.FindByEmailAsync(adminEmail) is { } configured && !await users.IsInRoleAsync(configured, Role))
            {
                await users.AddToRoleAsync(configured, Role);
                logger.LogWarning("{Email} was made administrator via Auth:AdminEmail", adminEmail);
            }
        }

        if ((await users.GetUsersInRoleAsync(Role)).Count > 0) return;
        var db = services.GetRequiredService<Data.WolselsDbContext>();
        var firstUserId = db.Households.OrderBy(h => h.Id).Select(h => h.CreatedByUserId).FirstOrDefault()
            ?? users.Users.OrderBy(u => u.Id).Select(u => u.Id).FirstOrDefault();
        if (firstUserId is not null && await users.FindByIdAsync(firstUserId) is { } first)
        {
            await users.AddToRoleAsync(first, Role);
            logger.LogInformation("{Email} was made administrator (first account)", first.Email);
        }
    }
}

public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>Checks the role in the database per request, so that revoking takes effect immediately.</summary>
public sealed class AdminRequirementHandler(UserManager<IdentityUser> users) : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (await users.GetUserAsync(context.User) is { } user && await users.IsInRoleAsync(user, AdminAuthorization.Role))
            context.Succeed(requirement);
    }
}
