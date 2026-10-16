using System.Security.Claims;

namespace Wolsels.Api.Services;

public interface ICurrentUser
{
    string? UserId { get; }

    /// <summary>Household of the logged-in user; looked up per request by <see cref="HouseholdMiddleware"/>.</summary>
    int? HouseholdId { get; }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public int? HouseholdId { get; set; }
}
