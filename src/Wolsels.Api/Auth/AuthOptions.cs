namespace Wolsels.Api.Auth;

public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Of nieuwe gebruikers zich zelf kunnen registreren. Het allereerste account mag altijd.</summary>
    public bool AllowRegistration { get; set; } = true;

    /// <summary>Max. pogingen per IP-adres per minuut op inloggen, registreren, 2FA en wachtwoordacties.</summary>
    public int RateLimitPerMinute { get; set; } = 10;

    /// <summary>Max. pogingen per IP-adres per uur op dezelfde endpoints.</summary>
    public int RateLimitPerHour { get; set; } = 60;

    /// <summary>Max. aanvragen voor een herstelmail per IP-adres per uur.</summary>
    public int PasswordResetPerIpPerHour { get; set; } = 5;

    /// <summary>Max. herstelmails per e-mailadres per uur (meer aanvragen worden stil genegeerd).</summary>
    public int PasswordResetPerEmailPerHour { get; set; } = 3;

    /// <summary>Aantal foute pogingen (wachtwoord of 2FA-code) voordat een account tijdelijk geblokkeerd wordt.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;
}
