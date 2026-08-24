// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Persistence;

/// <summary>Indicates that a local write lost its connection while committing, so whether it became durable is unknown.</summary>
/// <remarks>
/// <para>
/// This is the one persistence failure a retry may not resolve, and it is separated from
/// <see cref="PersistenceTransientFailureException" /> for exactly that reason. A failure raised while staging or
/// while saving happened before <c>COMMIT</c> was ever sent, so the write provably did not become durable and the
/// unit of work can be staged again. A failure raised by the commit round trip itself is a client that stopped
/// hearing from a server that may already have committed, and staging the work again would apply it a second time —
/// a spend total that accumulates and an audit row that is inserted blind are both written that way.
/// </para>
/// <para>
/// The same distinction is drawn for outgoing mail, where a connection lost after the message data leaves the client
/// unable to tell an accepted message from a rejected one, and repeating the send risks a second copy. What answers
/// it there is the outbox, which re-drives under an idempotency of its own; what answers it here is the caller,
/// which knows whether its own write can be repeated against a row that may already carry it.
/// </para>
/// <para>
/// The message carries no provider details, SQL, tracked values, or personal data. What the database actually said
/// stays reachable as <see cref="Exception.InnerException" /> for a log an operator reads.
/// </para>
/// </remarks>
public sealed class PersistenceCommitOutcomeUnknownException : MailFathomException
{
    /// <summary>Initializes a new unknown commit outcome over the provider failure that produced it.</summary>
    /// <param name="operatorSafeMessage">A message free of provider details, tracked values, and personal data.</param>
    /// <param name="providerFailure">The provider failure the commit round trip met.</param>
    public PersistenceCommitOutcomeUnknownException(string operatorSafeMessage, Exception providerFailure)
        : base(operatorSafeMessage, providerFailure)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.PersistenceCommitOutcomeUnknown;
}
