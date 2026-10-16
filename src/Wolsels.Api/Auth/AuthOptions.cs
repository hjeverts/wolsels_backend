namespace Wolsels.Api.Auth;

public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Whether new users can sign up themselves. The very first account is always allowed.</summary>
    public bool AllowRegistration { get; set; } = true;

    /// <summary>Max. attempts per IP address per minute on login, sign-up, 2FA and password actions.</summary>
    public int RateLimitPerMinute { get; set; } = 10;

    /// <summary>Max. attempts per IP address per hour on the same endpoints.</summary>
    public int RateLimitPerHour { get; set; } = 60;

    /// <summary>Max. password reset mail requests per IP address per hour.</summary>
    public int PasswordResetPerIpPerHour { get; set; } = 5;

    /// <summary>Max. reset mails per email address per hour (further requests are silently ignored).</summary>
    public int PasswordResetPerEmailPerHour { get; set; } = 3;

    /// <summary>Number of failed attempts (password or 2FA code) before an account is temporarily locked.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    /// <summary>This account always becomes an administrator at startup (to restore access).</summary>
    public string? AdminEmail { get; set; }
}
