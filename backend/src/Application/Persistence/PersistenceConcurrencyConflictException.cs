// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Persistence;

/// <summary>Indicates that a local write did not commit because another writer changed the same durable state.</summary>
/// <remarks>
/// <para>
/// A conflict is an ordinary loop outcome only inside <see cref="OptimisticConcurrencyRetryPolicy" />, where
/// <see cref="PersistenceCommitResult" /> keeps it visible in the commit signature. Once no further attempt is
/// allowed, the fact has to travel through use-case code that cannot make a concurrency decision, so it is raised
/// instead of restated as a result value at every intermediate boundary.
/// </para>
/// <para>
/// The exception carries no provider details, SQL, tracked values, entity state, or personal data. Callers that can
/// decide what a conflict means catch it at a named boundary; everything in between propagates it unchanged.
/// </para>
/// </remarks>
public sealed class PersistenceConcurrencyConflictException : MailFathomException
{
    /// <summary>Initializes a new persistence concurrency conflict with a message that names the conflicting write.</summary>
    /// <param name="operatorSafeMessage">A message free of provider details, tracked values, and personal data.</param>
    public PersistenceConcurrencyConflictException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.PersistenceConcurrencyConflict;
}
