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
    /// Zorgt dat de beheerdersrol bestaat en dat er minstens één beheerder is: bij een bestaande installatie
    /// wordt de maker van het eerste huishouden beheerder. Daarnaast wordt Auth:AdminEmail (indien ingesteld)
    /// altijd beheerder, om toegang te kunnen herstellen.
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
                logger.LogWarning("{Email} is beheerder gemaakt via Auth:AdminEmail", adminEmail);
            }
        }

        if ((await users.GetUsersInRoleAsync(Role)).Count > 0) return;
        var db = services.GetRequiredService<Data.WolselsDbContext>();
        var firstUserId = db.Households.OrderBy(h => h.Id).Select(h => h.CreatedByUserId).FirstOrDefault()
            ?? users.Users.OrderBy(u => u.Id).Select(u => u.Id).FirstOrDefault();
        if (firstUserId is not null && await users.FindByIdAsync(firstUserId) is { } first)
        {
            await users.AddToRoleAsync(first, Role);
            logger.LogInformation("{Email} is beheerder gemaakt (eerste account)", first.Email);
        }
    }
}

public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>Controleert de rol per verzoek in de database, zodat intrekken direct effect heeft.</summary>
public sealed class AdminRequirementHandler(UserManager<IdentityUser> users) : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (await users.GetUserAsync(context.User) is { } user && await users.IsInRoleAsync(user, AdminAuthorization.Role))
            context.Succeed(requirement);
    }
}
