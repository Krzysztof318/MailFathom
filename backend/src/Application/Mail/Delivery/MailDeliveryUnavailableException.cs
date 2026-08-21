// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery;

/// <summary>Indicates that a submission server did not serve an operation within the resilience budget configured for it.</summary>
/// <remarks>
/// <para>
/// This is the failure a caller sees when the attempts, the timeouts, or the in-flight limit of the delivery
/// dependency class were spent: an abandoned attempt, an operation that outlived its total timeout, an open circuit,
/// and a shed execution all arrive here. The submission endpoint is unreachable for now and the same work is expected
/// to succeed later.
/// </para>
/// <para>
/// It is deliberately distinct from <see cref="OperationCanceledException" />, which stays the failure of a caller
/// that stopped waiting — a host shutting down — and from a phase budget expiring, which arrives as a
/// <see cref="TimeoutException" /> naming the phase and says the server answered nothing rather than that the whole
/// dependency is spent.
/// </para>
/// <para>
/// The message names the account alias and nothing else. A submission failure knows a host, a credential, and the
/// recipients an exchange had reached, and none of those may appear in a line an operator's log keeps.
/// </para>
/// </remarks>
public sealed class MailDeliveryUnavailableException : MailFathomException
{
    /// <summary>Initializes a new delivery unavailability failure naming the account it stopped.</summary>
    /// <param name="accountId">The account whose submission server did not serve the operation.</param>
    /// <param name="innerException">The rejection the resilience pipeline produced.</param>
    public MailDeliveryUnavailableException(MailAccountId accountId, Exception innerException)
        : base(
            $"The submission server for {accountId.Value} did not serve the operation within its configured resilience budget.",
            innerException) =>
        this.AccountId = accountId;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailDeliveryUnavailable;

    /// <summary>Gets the account whose submission server was unavailable.</summary>
    public MailAccountId AccountId { get; }
}
