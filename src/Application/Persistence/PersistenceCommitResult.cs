// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Persistence;

/// <summary>Describes whether a persistence session committed or detected a concurrent write.</summary>
public enum PersistenceCommitResult
{
    /// <summary>The transaction committed successfully.</summary>
    Committed,

    /// <summary>The transaction did not commit because persisted state changed after it was read.</summary>
    ConcurrencyConflict,
}
