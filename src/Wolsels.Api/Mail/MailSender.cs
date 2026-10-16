using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Wolsels.Api.Mail;

public interface IMailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string to, string subject, string text, string html, CancellationToken ct = default);
}

public class SmtpMailSender(IOptions<SmtpOptions> options, ILogger<SmtpMailSender> logger) : IMailSender
{
    private readonly SmtpOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendAsync(string to, string subject, string text, string html, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("No mail server has been configured.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.From!));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { TextBody = text, HtmlBody = html }.ToMessageBody();

        var security = _options.Security.ToLowerInvariant() switch
        {
            "none" => SecureSocketOptions.None,
            "starttls" => SecureSocketOptions.StartTls,
            "sslonconnect" or "ssl" or "tls" => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.Auto,
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host!, _options.Port, security, ct);
        if (!string.IsNullOrEmpty(_options.Username))
            await client.AuthenticateAsync(_options.Username, _options.Password ?? "", ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
        logger.LogInformation("Email '{Subject}' sent", subject);
    }
}
