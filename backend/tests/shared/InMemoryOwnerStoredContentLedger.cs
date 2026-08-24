// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Keeps what each owner's stored content holds in memory, so a per-owner ceiling has a figure to read.</summary>
/// <remarks>
/// Hand-written rather than substituted, because a ceiling test arranges a figure and then asserts against what the
/// ceiling did with it: a substitute would answer from a script and the assertion would be about the script.
/// </remarks>
internal sealed class InMemoryOwnerStoredContentLedger : IOwnerStoredContentLedger
{
    private readonly Dictionary<MailOwnerId, long> storedBytesByOwner = [];

    /// <summary>Gets how many times a figure was read, which is what says a ceiling asked once per run.</summary>
    public int ReadCount { get; private set; }

    /// <summary>Gets how many times a figure was re-derived rather than read.</summary>
    public int RederiveCount { get; private set; }

    /// <summary>States what one owner's stored content holds before the test begins.</summary>
    /// <param name="owner">The owner.</param>
    /// <param name="storedBytes">What their payloads hold.</param>
    /// <returns>This ledger, so arrangements read as one expression.</returns>
    public InMemoryOwnerStoredContentLedger Holding(MailOwnerId owner, long storedBytes)
    {
        this.storedBytesByOwner[owner] = storedBytes;

        return this;
    }

    /// <inheritdoc />
    public Task<long> ReadStoredContentBytesAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.ReadCount++;

        return Task.FromResult(this.storedBytesByOwner.GetValueOrDefault(owner));
    }

    /// <inheritdoc />
    public Task<long> RederiveStoredContentBytesAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.RederiveCount++;

        return Task.FromResult(this.storedBytesByOwner.GetValueOrDefault(owner));
    }
}
