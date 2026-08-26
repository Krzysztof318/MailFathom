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

    /// <inheritdoc />
    public Task<RootSettingsDocument> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new RootSettingsDocument(this.Json, this.Version));

    /// <inheritdoc />
    /// <remarks>The version in the condition is the whole of the concurrency model, exactly as the statement's own <c>WHERE</c> clause is.</remarks>
    public Task<long?> CommitAsync(string json, long expectedVersion, CancellationToken cancellationToken)
    {
        if (expectedVersion != this.Version)
        {
            return Task.FromResult<long?>(null);
        }

        this.Json = json;
        this.Version++;
        this.AcceptedCommits++;

        return Task.FromResult<long?>(this.Version);
    }
}
