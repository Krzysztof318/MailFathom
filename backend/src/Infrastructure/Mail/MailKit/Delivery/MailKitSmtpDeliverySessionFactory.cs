// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Resilience;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>MailKit-backed factory for the one session able to reach a submission server.</summary>
/// <remarks>
/// It opens a connection per session and pools nothing, for the reason the port states: a submission is one exchange
/// whose failure must never be repeated on a caller's behalf, so there is no connection worth keeping warm between two
/// of them. The session is returned already authenticated, so a credential a deployment got wrong is a failure at the
/// moment delivery is attempted rather than one discovered part-way through transmitting a message.
/// </remarks>
internal sealed class MailKitSmtpDeliverySessionFactory(
    Func<ISubmissionClient> clientFactory,
    Func<string, int, CancellationToken, Task<Socket>> socketConnector,
    ISmtpAccountSettingsProvider settingsProvider,
    IMailAccessTokenSource accessTokenSource,
    OutboundOperationExecutor operationExecutor,
    ITransientFailureClassifier transientFailureClassifier,
    MailDeliveryTimeouts timeouts,
    MailDeliveryTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<MailKitSmtpConnection> logger) : IMailDeliverySessionFactory
{
    /// <inheritdoc />
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the connection passes to the returned session; an establishment failure disposes it here instead.")]
    public async Task<IMailDeliverySession> OpenForDeliveryAsync(
        MailAccountId accountId,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);

        var connection = new MailKitSmtpConnection(
            clientFactory,
            socketConnector,
            settingsProvider,
            accessTokenSource,
            operationExecutor,
            transientFailureClassifier,
            accountId,
            transportSecurityPolicy,
            timeouts,
            timeProvider,
            logger);

        try
        {
            await connection.EstablishAsync(cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();

            throw;
        }

        return new MailKitSmtpDeliverySession(connection, accountId, telemetry);
    }
}
