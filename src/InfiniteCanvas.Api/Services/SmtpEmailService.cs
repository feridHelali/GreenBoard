using System.Net;
using System.Net.Mail;

namespace InfiniteCanvas.Api.Services;

/// <summary>
/// Service d'envoi d'e-mails (confirmation, r\u00e9initialisation de mot de passe).
/// En d\u00e9veloppement, les e-mails sont journaux dans la console.
/// En production, configurez Resend, Brevo, ou un SMTP propre.
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task SendConfirmationAsync(string email, string token)
    {
        var link = $"{_config["App__BaseUrl"] ?? "https://localhost:5001"}/auth/confirm?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        _logger.LogInformation("E-mail de confirmation \u00e0 {Email} : {Link}", email, link);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string email, string token)
    {
        var link = $"{_config["App__BaseUrl"] ?? "https://localhost:5001"}/auth/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        _logger.LogInformation("E-mail de r\u00e9initialisation \u00e0 {Email} : {Link}", email, link);
        return Task.CompletedTask;
    }
}
