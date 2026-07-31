// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Application.Persistence;

/// <summary>Describes whether a persistence session committed or detected a concurrent write.</summary>
public enum PersistenceCommitResult
{
    /// <summary>The transaction committed successfully.</summary>
    Committed = 0,

    /// <summary>The transaction did not commit because persisted state changed after it was read.</summary>
    ConcurrencyConflict = 1,
}
