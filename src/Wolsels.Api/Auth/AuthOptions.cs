namespace Wolsels.Api.Auth;

public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Of nieuwe gebruikers zich zelf kunnen registreren. Het allereerste account mag altijd.</summary>
    public bool AllowRegistration { get; set; } = true;
}
