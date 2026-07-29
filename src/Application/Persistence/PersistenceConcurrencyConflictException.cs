// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Failures;

namespace MailMcp.Application.Persistence;

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
public sealed class PersistenceConcurrencyConflictException : MailMcpException
{
    /// <summary>Initializes a new persistence concurrency conflict with a message that names the conflicting write.</summary>
    /// <param name="operatorSafeMessage">A message free of provider details, tracked values, and personal data.</param>
    public PersistenceConcurrencyConflictException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailMcpErrorCode ErrorCode => MailMcpErrorCode.PersistenceConcurrencyConflict;
}
