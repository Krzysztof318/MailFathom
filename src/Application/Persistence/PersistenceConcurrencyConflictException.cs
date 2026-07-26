// Copyright © 2026 Krzysztof Kasprowicz

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
public sealed class PersistenceConcurrencyConflictException : Exception
{
    private const string DefaultMessage = "A local write did not commit because another writer changed the same durable state.";

    /// <summary>Initializes a new persistence concurrency conflict.</summary>
    public PersistenceConcurrencyConflictException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Initializes a new persistence concurrency conflict with a message that names the conflicting write.</summary>
    /// <param name="message">A message free of provider details, tracked values, and personal data.</param>
    public PersistenceConcurrencyConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new persistence concurrency conflict with a safe message and inner exception.</summary>
    /// <param name="message">A message free of provider details, tracked values, and personal data.</param>
    /// <param name="innerException">The failure that revealed the conflict.</param>
    public PersistenceConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
