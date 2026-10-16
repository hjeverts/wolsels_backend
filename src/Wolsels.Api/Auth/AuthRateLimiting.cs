using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Wolsels.Api.Localization;

namespace Wolsels.Api.Auth;

/// <summary>
/// Beperkt het aantal pogingen per IP-adres op gevoelige endpoints, bovenop de blokkering per account.
/// </summary>
public static class AuthRateLimiting
{
    public const string PasswordResetPolicy = "password-reset";

    /// <summary>Endpoints waar wachtwoorden, codes of mails mee gemoeid zijn.</summary>
    private static readonly string[] SensitivePaths =
    [
        "/api/auth/login", "/api/auth/register", "/api/auth/password", "/api/auth/manage/2fa", "/api/auth/manage/info",
        "/api/household/invites", "/api/household/join",
    ];

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, AuthOptions options)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    status = 429,
                    title = Texts.Get("Auth.TooManyAttempts"),
                }, ct);
            };

            // Per minuut én per uur, zodat ook langzaam en volhardend raden begrensd is.
            o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                Limiter(options.RateLimitPerMinute, TimeSpan.FromMinutes(1)),
                Limiter(options.RateLimitPerHour, TimeSpan.FromHours(1)));

            o.AddPolicy(PasswordResetPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PasswordResetPerIpPerHour,
                    Window = TimeSpan.FromHours(1),
                }));
        });
        return services;
    }

    private static PartitionedRateLimiter<HttpContext> Limiter(int permits, TimeSpan window) =>
        PartitionedRateLimiter.Create<HttpContext, string>(http =>
            IsSensitive(http.Request.Path)
                ? RateLimitPartition.GetSlidingWindowLimiter(ClientKey(http), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = window,
                    SegmentsPerWindow = 6,
                })
                : RateLimitPartition.GetNoLimiter("onbeperkt"));

    private static bool IsSensitive(PathString path) =>
        SensitivePaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private static string ClientKey(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "onbekend";
}
