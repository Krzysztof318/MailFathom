// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Transmission;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>A submission server reached over one established connection, and what that server said it will accept.</summary>
/// <remarks>
/// The session is the port and the connection is the transport, kept apart for the reason the mailbox adapter keeps
/// them apart: what a caller may ask for is decided by this type's surface, while how the server is reached, bounded,
/// and given up on belongs to the connection underneath. The connection is owned here and closed with the session.
/// </remarks>
internal sealed class MailKitSmtpDeliverySession(
    MailKitSmtpConnection connection,
    MailAccountId accountId,
    MailDeliveryTelemetry telemetry) : IMailDeliverySession
{
    /// <inheritdoc />
    public MailDeliveryCapabilities Capabilities => connection.Capabilities;

    /// <inheritdoc />
    /// <remarks>
    /// The span covers the exchange with the server and nothing around it, which is what makes it answer the question
    /// it is for: whether the provider is the slow part. A refusal completes it rather than failing it — the server
    /// answered, so the exchange did what it was for, and what the answer was belongs to the send's own record. A
    /// caller that stopped waiting and a host that is shutting down leave it unset for the same reason read the other
    /// way: neither is the provider failing, and only a failed exchange marks the span as one.
    /// </remarks>
    public async Task<MailTransmission> TransmitAsync(
        MailTransmissionRequest request,
        MailEnvelopeLedger envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var submission = telemetry.BeginSubmission(accountId, request.OutgoingEmailId);

        try
        {
            var transmission = await connection.TransmitAsync(request, envelope, cancellationToken);

            submission.Completed();

            return transmission;
        }
        catch (OperationCanceledException)
        {
            submission.Abandoned();

            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
