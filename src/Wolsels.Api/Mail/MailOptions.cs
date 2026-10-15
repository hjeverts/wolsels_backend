namespace Wolsels.Api.Mail;

public class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>Hostnaam van de mailserver. Leeg = geen mail, en dus geen "wachtwoord vergeten".</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    /// <summary>Auto, None, StartTls of SslOnConnect.</summary>
    public string Security { get; set; } = "Auto";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public string FromName { get; set; } = "Wolsels";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

public class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Openbare URL van Wolsels (bv. https://wol.example.nl), gebruikt voor links in e-mails.</summary>
    public string? PublicUrl { get; set; }
}
