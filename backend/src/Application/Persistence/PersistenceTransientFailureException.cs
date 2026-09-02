// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Persistence;

/// <summary>Indicates that a local write did not commit because the database failed in a way that can clear on its own.</summary>
/// <remarks>
/// <para>
/// This is the conflict's counterpart one failure class over, and it exists for the same reason: a transient failure
/// is an ordinary loop outcome only inside <see cref="OptimisticConcurrencyRetryPolicy" />, which stages the unit of
/// work again from a fresh read on a connection the pool opens anew. Nothing below that can repeat the work, because
/// a dropped connection takes the whole transaction with it — repeating the statement alone would meet a transaction
/// the server has already discarded.
/// </para>
/// <para>
/// Deciding which provider failures can clear on their own belongs where the provider's exceptions are, so the
/// session raises this and the classification stays in one place rather than being restated here.
/// </para>
/// <para>
/// The message carries no provider details, SQL, tracked values, or personal data. What the database actually said
/// stays reachable as <see cref="Exception.InnerException" /> for a log an operator reads, and no caller between the
/// session and the retry policy has to name a provider exception to recognize it.
/// </para>
/// </remarks>
public sealed class PersistenceTransientFailureException : MailFathomException
{
    /// <summary>Initializes a new transient persistence failure over the provider failure that produced it.</summary>
    /// <param name="operatorSafeMessage">A message free of provider details, tracked values, and personal data.</param>
    /// <param name="providerFailure">The provider failure the commit met.</param>
    public PersistenceTransientFailureException(string operatorSafeMessage, Exception providerFailure)
        : base(operatorSafeMessage, providerFailure)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.PersistenceTransientFailure;
}
