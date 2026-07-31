// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.EmailContent;

namespace MailMcp.Application.Emails;

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
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The metadata, or the reason the message could not be read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Content that does not parse, declares more parts than the configured limit, or nests deeper than it returns as a
    /// failure result rather than as an exception, so one unreadable message never stops a synchronization batch.
    /// </remarks>
    Task<EmailMimeExtractionResult> ReadMetadataAsync(RemoteEmailContent content, CancellationToken cancellationToken);
}
