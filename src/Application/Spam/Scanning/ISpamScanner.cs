// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Application.Spam.Scanning;

/// <summary>Scores one whole message against a scanner's rule corpus.</summary>
/// <remarks>
/// <para>
/// The port is application-owned and narrow on purpose. It takes the raw RFC 822 message and answers with a score, a
/// threshold, the names of what fired, and the corpus those came from; no protocol type, socket type, or scanner
/// vocabulary crosses it in either direction. What sits behind it is a deployment decision, and the deployment that
/// registers nothing is the default — the deterministic stage works alone, so an unimplemented port costs a
/// classification its second opinion and nothing else.
/// </para>
/// <para>
/// An implementation sends the whole message to a scanner, which is a processing decision an operator has to have taken
/// deliberately: the content is personal data and the scanner is a separate process. It reads content that is already
/// stored locally, so it opens no IMAP session and cannot affect a remote <c>\Seen</c> flag, and it must bound its own
/// call — a scanner that stops answering degrades a signal rather than stalling a run.
/// </para>
/// </remarks>
public interface ISpamScanner
{
    /// <summary>Scores one message.</summary>
    /// <param name="content">The raw MIME already stored for the occurrence.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The score and what produced it, or the reason nothing was scored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A scanner that cannot be reached, does not answer within its bound, or answers unintelligibly is reported as
    /// <see cref="SpamScanOutcome.Unavailable" /> rather than raised, because the caller continues with the
    /// deterministic verdict either way.
    /// </remarks>
    Task<SpamScanResult> ScanAsync(StoredEmailContent content, CancellationToken cancellationToken);
}
