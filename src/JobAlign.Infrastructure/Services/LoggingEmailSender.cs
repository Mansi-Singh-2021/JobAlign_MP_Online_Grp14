using JobAlign.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace JobAlign.Infrastructure.Services;

/// <summary>
/// Development stand-in for real email: writes the message to the application log
/// instead of sending it.
/// </summary>
/// <remarks>
/// FR-05 requires password reset "through a verified email link". The token and the
/// link are real and are verified by Identity; only delivery is stubbed. Swap this
/// registration for an SMTP or transactional-email implementation and the reset flow
/// works unchanged — that separation is the point of <see cref="IAppEmailSender"/>.
///
/// Not suitable for production: reset links in logs are a credential leak.
/// </remarks>
public class LoggingEmailSender : IAppEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Email delivery is stubbed. Would send to {ToEmail} with subject {Subject}.\n{Body}",
            toEmail, subject, body);

        return Task.CompletedTask;
    }
}
