// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Application.Spam.Signals;

/// <summary>Reads the spam-relevant headers out of one message's raw MIME.</summary>
/// <remarks>
/// <para>
/// The port exists because parsing RFC 8601 <c>Authentication-Results</c> and the ARC chain of RFC 8617 needs a MIME
/// library, and no library type may cross into the application. What comes back is uninterpreted: the
/// outcomes as the server wrote them and the provider headers as the message carried them, so the reading of what they
/// mean happens above this line where it is unit-testable and where the two sources can be weighed against each other.
/// </para>
/// <para>
/// It reads content already stored locally, so it costs no IMAP round trip and cannot affect a remote <c>\Seen</c> flag.
/// A message whose MIME does not parse is answered with <see cref="SpamHeaderFacts.None" /> rather than an exception: a
/// classification with no header facts is a real outcome, and one unreadable message must not stop a run.
/// </para>
/// </remarks>
public interface IEmailSpamHeaderReader
{
    /// <summary>Reads one message's spam-relevant headers.</summary>
    /// <param name="content">The raw MIME already stored for the occurrence.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The facts the headers carried, or <see cref="SpamHeaderFacts.None" /> when the content carried or yielded none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content" /> is <see langword="null" />.</exception>
    Task<SpamHeaderFacts> ReadAsync(StoredEmailContent content, CancellationToken cancellationToken);
}
