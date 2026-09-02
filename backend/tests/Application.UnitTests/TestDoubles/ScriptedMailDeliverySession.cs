// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Transmission;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Stands in for the one session able to reach a submission server, with the exchange written by the test.</summary>
/// <remarks>
/// The script is handed the envelope ledger, because what a submission does to that ledger before it ends is exactly
/// what the caller reads to decide whether anything was transmitted. A double that only returned a result could not
/// express the case the whole design is about: a server that accepted an address and then answered nothing.
/// </remarks>
/// <param name="transmit">Runs the exchange the test is about, and fills the ledger the way a server's replies would.</param>
/// <param name="capabilities">What the server declared it will accept.</param>
internal sealed class ScriptedMailDeliverySession(
    Func<MailTransmissionRequest, MailEnvelopeLedger, CancellationToken, Task<MailTransmission>> transmit,
    MailDeliveryCapabilities? capabilities = null) : IMailDeliverySession, IMailDeliverySessionFactory
{
    /// <summary>Gets every request the caller offered, which is what proves a retry offers only the outstanding addresses.</summary>
    internal List<MailTransmissionRequest> Transmitted { get; } = [];

    /// <summary>Gets whether the session was disposed, which a caller owes for every session it opens.</summary>
    internal bool Disposed { get; private set; }

    /// <inheritdoc />
    public MailDeliveryCapabilities Capabilities { get; } =
        capabilities ?? new MailDeliveryCapabilities(MaxMessageBytes: null, true, true);

    /// <inheritdoc />
    public Task<IMailDeliverySession> OpenForDeliveryAsync(
        MailAccountId accountId,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken) => Task.FromResult<IMailDeliverySession>(this);

    /// <inheritdoc />
    public Task<MailTransmission> TransmitAsync(
        MailTransmissionRequest request,
        MailEnvelopeLedger envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(envelope);

        this.Transmitted.Add(request);

        return transmit(request, envelope, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.Disposed = true;

        return ValueTask.CompletedTask;
    }
}
