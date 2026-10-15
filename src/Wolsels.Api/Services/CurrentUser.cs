using System.Security.Claims;

namespace Wolsels.Api.Services;

public interface ICurrentUser
{
    string? UserId { get; }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
