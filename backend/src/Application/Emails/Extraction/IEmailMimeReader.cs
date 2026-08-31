// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Emails.Extraction;

/// <summary>Turns raw RFC 822 content into the normalized metadata the local read side needs.</summary>
/// <remarks>
/// <para>
/// The port exists so the MIME library stays inside its adapter: participants, thread identifiers, and the attachment
/// summary cross into the application as domain values, and nothing above this interface handles a parser type.
/// </para>
/// <para>
/// Implementations read content that was already fetched, so extraction costs no IMAP round trip and cannot affect a
/// remote <c>\Seen</c> flag. They must never materialize attachment content: per-attachment size is measured by
/// streaming the part and discarding what it holds.
/// </para>
/// </remarks>
public interface IEmailMimeReader
{
    /// <summary>Reads one message's normalized metadata.</summary>
    /// <param name="content">The raw MIME already fetched for the occurrence.</param>
    /// <param name="owner">The owner the message belongs to, which every path reaching this port already holds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The metadata, or the reason the message could not be read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Content that does not parse, declares more parts than the configured limit, or nests deeper than it returns as a
    /// failure result rather than as an exception, so one unreadable message never stops a synchronization batch.
    /// </para>
    /// <para>
    /// The owner is here rather than on <see cref="RemoteEmailContent" /> because it is a fact about the derivation
    /// rather than about the bytes: what decides how a body is redacted before anything is derived from it is whose
    /// mail it is, and the decorator that applies that decision sits on this port. Every caller holds the answer —
    /// synchronization is running one owner's account and a re-derivation walk carries it on each row — so nothing
    /// resolves it a second time.
    /// </para>
    /// </remarks>
    Task<EmailMimeExtractionResult> ReadMetadataAsync(
        RemoteEmailContent content,
        MailOwnerId owner,
        CancellationToken cancellationToken);
}
