// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.EmailContent;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Mcp.UnitTests;

/// <summary>Answers a content read with one fixed stored payload and records that it was asked.</summary>
/// <remarks>
/// Writing is unimplemented on purpose. A read never stores content, so a stub that quietly accepted a write would let
/// a boundary that started writing pass unnoticed.
/// </remarks>
internal sealed class StubEmailContentStore(StoredEmailContent? storedContent = null) : IEmailContentStore
{
    /// <summary>Gets how many content reads were issued, so a test can prove a refusal never reached storage.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc />
    public Task SaveContentAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        RemoteEmailContent content,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Reading an email never stores content.");

    /// <inheritdoc />
    public Task<StoredEmailContent?> FindStoredContentAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.ReadCount++;

        return Task.FromResult(storedContent);
    }
}
