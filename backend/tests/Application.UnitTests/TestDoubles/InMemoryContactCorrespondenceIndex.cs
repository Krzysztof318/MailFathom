// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers a contact correlation from what a test put in it, and records what it was asked.</summary>
/// <remarks>
/// Hand-written rather than substituted because both halves of what a test asserts here are about the call itself: the
/// window the use case measured, and the addresses it resolved off the contact. A substitute would carry those in an
/// argument matcher instead of in a value a test can read.
/// </remarks>
internal sealed class InMemoryContactCorrespondenceIndex : IContactCorrespondenceIndex
{
    private readonly List<CorrespondingThread> threads = [];
    private readonly List<CorrespondingDocument> documents = [];
    private readonly List<CorrespondenceRead> reads = [];

    /// <summary>Gets every read this index was asked for, in the order they were asked.</summary>
    public IReadOnlyList<CorrespondenceRead> Reads => this.reads;

    /// <summary>Holds the conversations a read answers with.</summary>
    public InMemoryContactCorrespondenceIndex WithThreads(params CorrespondingThread[] correspondingThreads)
    {
        this.threads.AddRange(correspondingThreads);

        return this;
    }

    /// <summary>Holds the documents a read answers with.</summary>
    public InMemoryContactCorrespondenceIndex WithDocuments(params CorrespondingDocument[] correspondingDocuments)
    {
        this.documents.AddRange(correspondingDocuments);

        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CorrespondingThread>> ReadRecentThreadsAsync(
        MailboxScope scope,
        IReadOnlyList<string> normalizedAddresses,
        DateTimeOffset correspondedOnOrAfter,
        CancellationToken cancellationToken)
    {
        this.reads.Add(new CorrespondenceRead(scope, normalizedAddresses, correspondedOnOrAfter));

        return Task.FromResult<IReadOnlyList<CorrespondingThread>>([.. this.threads]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CorrespondingDocument>> ReadRecentDocumentsAsync(
        MailboxScope scope,
        IReadOnlyList<string> normalizedAddresses,
        DateTimeOffset correspondedOnOrAfter,
        CancellationToken cancellationToken)
    {
        this.reads.Add(new CorrespondenceRead(scope, normalizedAddresses, correspondedOnOrAfter));

        return Task.FromResult<IReadOnlyList<CorrespondingDocument>>([.. this.documents]);
    }

    /// <summary>One read this index was asked for.</summary>
    /// <param name="Scope">The scope the use case narrowed by.</param>
    /// <param name="NormalizedAddresses">The addresses it resolved off the contact.</param>
    /// <param name="CorrespondedOnOrAfter">The start of the window it measured.</param>
    internal sealed record CorrespondenceRead(
        MailboxScope Scope,
        IReadOnlyList<string> NormalizedAddresses,
        DateTimeOffset CorrespondedOnOrAfter);
}
