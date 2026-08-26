// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Settings;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The singleton persisted-configuration row, as the one document and version a read and a commit share.</summary>
/// <remarks>
/// Both sides of the row are one object because the behaviour worth asserting is what happens between them: a commit
/// that lost the race, a refusal leaving the document exactly as it was, and a reload observing the version the commit
/// produced. Two substitutes scripted independently could be made to say anything about that, including things a
/// database never would.
/// </remarks>
internal sealed class InMemoryRootSettingsRow(string json, long version)
    : IRootSettingsDocumentReader, IRootSettingsDocumentWriter
{
    /// <summary>Gets the document the row holds.</summary>
    public string Json { get; private set; } = json;

    /// <summary>Gets the version the row stands at.</summary>
    public long Version { get; private set; } = version;

    /// <summary>Gets how many commits the row accepted, which is what a refusal that changed nothing leaves at zero.</summary>
    public int AcceptedCommits { get; private set; }

    /// <summary>Gets or sets what happens the moment a commit has been accepted, which is where a caller can give up.</summary>
    /// <remarks>
    /// The seam a cancellation claim needs, because the moment it is about — after the row moved and before the layer
    /// republishes — cannot be reached from outside the write. Nothing else uses it, and a test that sets none is
    /// unaffected.
    /// </remarks>
    public Action? WhenCommitted { get; set; }

    /// <inheritdoc />
    /// <remarks>The token is observed because the real reader hands it to Npgsql, which throws on one already cancelled.</remarks>
    public Task<RootSettingsDocument> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new RootSettingsDocument(this.Json, this.Version));
    }

    /// <inheritdoc />
    /// <remarks>The version in the condition is the whole of the concurrency model, exactly as the statement's own <c>WHERE</c> clause is.</remarks>
    public Task<long?> CommitAsync(string json, long expectedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (expectedVersion != this.Version)
        {
            return Task.FromResult<long?>(null);
        }

        this.Json = json;
        this.Version++;
        this.AcceptedCommits++;

        this.WhenCommitted?.Invoke();

        return Task.FromResult<long?>(this.Version);
    }
}
