// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Stands in for the session an in-memory store is handed and never reads.</summary>
/// <remarks>
/// The doubles under <c>tests/shared</c> take a session because the port they implement does, and they hold their
/// records in a dictionary rather than in a transaction — so nothing here is ever called. It is written by hand rather
/// than substituted because this project deliberately carries the packages the shared files themselves need and no
/// others, and a session that is only ever passed along is not worth a dependency; committing through it is a fault in
/// the test rather than a case to answer, which is what the refusal below says.
/// </remarks>
internal sealed class IgnoredPersistenceSession : IPersistenceSession
{
    /// <inheritdoc />
    public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("An in-memory store commits nothing, so nothing commits this session.");

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
