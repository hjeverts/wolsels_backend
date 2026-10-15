using System.Security.Claims;

namespace Wolsels.Api.Services;

public interface ICurrentUser
{
    string? UserId { get; }

    /// <summary>Huishouden van de ingelogde gebruiker; per verzoek opgezocht door <see cref="HouseholdMiddleware"/>.</summary>
    int? HouseholdId { get; }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public int? HouseholdId { get; set; }
}
