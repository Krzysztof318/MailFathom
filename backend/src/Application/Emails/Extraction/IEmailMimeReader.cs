// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

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
    /// <param name="account">The user and the account the message belongs to, which every path reaching this port already holds.</param>
    /// <param name="rawMime">The raw MIME already fetched or already stored for the message.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The metadata, or the reason the message could not be read.</returns>
    /// <remarks>
    /// <para>
    /// Content that does not parse, declares more parts than the configured limit, or nests deeper than it returns as a
    /// failure result rather than as an exception, so one unreadable message never stops a synchronization batch.
    /// </para>
    /// <para>
    /// The account is named rather than the place a mail server holds the message, because it is a fact about the
    /// derivation rather than about the bytes: whose mail it is decides how a body is redacted and whose authentication
    /// statements are believed, while a stored message a server no longer holds is read exactly as one it still does.
    /// Every caller holds the answer — synchronization is running one user's account and a re-derivation walk carries it
    /// on each row — so nothing resolves it a second time.
    /// </para>
    /// </remarks>
    Task<EmailMimeExtractionResult> ReadMetadataAsync(
        MailAccountIdentity account,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken);
}
