namespace Wolsels.Api.Mail;

public class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>Host name of the mail server. Empty = no mail, and therefore no "forgot password".</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    /// <summary>Auto, None, StartTls or SslOnConnect.</summary>
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

    /// <summary>Public URL of Wolsels (e.g. https://wol.example.nl), used for links in emails.</summary>
    public string? PublicUrl { get; set; }
}
