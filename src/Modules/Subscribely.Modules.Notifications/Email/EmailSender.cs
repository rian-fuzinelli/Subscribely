using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Subscribely.Modules.Notifications.Email;

internal sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public string From { get; set; } = "no-reply@subscribely.local";

    public bool UseTls { get; set; }

    /// <summary>Desligado: os e-mails são registrados como <c>Skipped</c>, sem envio.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

internal sealed class NotificationsOptions
{
    public const string Section = "Notifications";

    /// <summary>Base dos links dos e-mails (portal do cliente).</summary>
    public Uri PortalBaseUrl { get; set; } = new("http://localhost:3000/");
}

internal sealed record EmailMessage(string To, string Subject, string Html, string Text);

internal interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

internal sealed class MailKitEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var smtp = options.Value;

        using var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(smtp.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();

        using var client = new SmtpClient { Timeout = (int)smtp.Timeout.TotalMilliseconds };
        await client.ConnectAsync(smtp.Host, smtp.Port, smtp.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
