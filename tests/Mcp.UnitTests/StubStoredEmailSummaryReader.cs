// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Emails;
using MailFathom.Domain.Emails;

namespace MailFathom.Mcp.UnitTests;

/// <summary>Answers a lookup with one fixed summary, under the identity it was asked about.</summary>
/// <remarks>
/// <para>
/// The identity is echoed onto the summary because a real reader answers only for the row it was asked for, and a read
/// naming several emails would otherwise receive the same identity for all of them. The identity it was asked for is
/// also kept, because the tool owns the conversion from a caller's text into that identity and the value storage
/// received is the observable result of it. The count is kept so a test can prove that a refusal at the boundary never
/// reached storage at all.
/// </para>
/// <para>
/// Naming the known identities makes some of them absent, which is what a partial read is written against: the emails
/// outside the set are answered with nothing, exactly as an email this mailbox copy does not hold would be.
/// </para>
/// </remarks>
internal sealed class StubStoredEmailSummaryReader : IStoredEmailSummaryReader
{
    private readonly EmailSummary? summary;
    private readonly HashSet<StoredEmailId>? knownEmailIds;

    /// <summary>Initializes a reader that answers for every identity, or for none when no summary is given.</summary>
    /// <param name="summary">The summary to answer with, or <see langword="null" /> to hold no email at all.</param>
    public StubStoredEmailSummaryReader(EmailSummary? summary = null) => this.summary = summary;

    /// <summary>Initializes a reader that answers only for the named identities.</summary>
    /// <param name="summary">The summary to answer with.</param>
    /// <param name="knownEmailIds">The identities this mailbox copy holds.</param>
    public StubStoredEmailSummaryReader(EmailSummary summary, params StoredEmailId[] knownEmailIds)
    {
        this.summary = summary;
        this.knownEmailIds = [.. knownEmailIds];
    }

    /// <summary>Gets the identity the last lookup named, or <see langword="null" /> when nothing was looked up.</summary>
    public StoredEmailId? LastStoredEmailId { get; private set; }

    /// <summary>Gets how many lookups were issued.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc />
    public Task<EmailSummary?> FindAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.LastStoredEmailId = storedEmailId;
        this.ReadCount++;

        return Task.FromResult(
            this.summary is { } known && this.knownEmailIds?.Contains(storedEmailId) is null or true
                ? known with { StoredEmailId = storedEmailId }
                : null);
    }
}
