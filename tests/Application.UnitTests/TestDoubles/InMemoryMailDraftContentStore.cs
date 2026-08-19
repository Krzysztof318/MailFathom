// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds the MIME of drafts in memory, replacing a draft's message the way the real store does.</summary>
/// <remarks>
/// Everything but the draft half throws. A draft never stores arriving mail and never stores a send's payload, so a
/// caller that reached one of those would be doing something this double must not answer for silently.
/// </remarks>
internal sealed class InMemoryMailDraftContentStore : IEmailContentStore
{
    private readonly Dictionary<MailDraftId, byte[]> messages = [];

    /// <summary>Gets how many revisions were stored, which is what proves an edit replaced rather than added.</summary>
    internal int WriteCount { get; private set; }

    /// <summary>Reads what is stored for one draft, without going through the port.</summary>
    internal ReadOnlyMemory<byte> Peek(MailDraftId draftId) =>
        this.messages.TryGetValue(draftId, out var stored) ? stored : ReadOnlyMemory<byte>.Empty;

    /// <inheritdoc />
    public Task SaveContentAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        RemoteEmailContent content,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("A draft never stores arriving mail.");

    /// <inheritdoc />
    public Task<StoredEmailContent?> FindStoredContentAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("A draft never reads arriving mail.");

    /// <inheritdoc />
    public Task SaveOutgoingContentAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("A draft never stores a send's payload.");

    /// <inheritdoc />
    public Task<StoredEmailContent?> FindOutgoingContentAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("A draft never reads a send's payload.");

    /// <inheritdoc />
    public Task SaveRecurringSendDraftAsync(
        IPersistenceSession session,
        RecurringSendId recurringSendId,
        ReadOnlyMemory<byte> draftMime,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("A draft never declares a repeated send.");

    /// <inheritdoc />
    public Task<StoredEmailContent?> FindRecurringSendDraftAsync(
        RecurringSendId recurringSendId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("A draft never reads what a repeated send is built from.");

    /// <inheritdoc />
    public Task SaveMailDraftContentAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        this.messages[draftId] = rawMime.ToArray();
        this.WriteCount++;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<StoredEmailContent?> FindMailDraftContentAsync(
        MailDraftId draftId,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.messages.TryGetValue(draftId, out var stored)
            ? new StoredEmailContent(stored.AsMemory(), stored.LongLength, SHA256.HashData(stored).AsMemory())
            : null);
}
