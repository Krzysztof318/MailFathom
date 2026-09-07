// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Screening;

/// <summary>Reads back what a composed outgoing message says, so it can be screened before it is written down.</summary>
/// <remarks>
/// <para>
/// A port of its own rather than a reuse of the reader that renders stored mail, because the two are asked about
/// different artifacts under different rules. That one is handed a message a stranger sent, parses it under structural
/// limits written for hostile input, sanitizes its markup, and bounds what a reader may be shown. This one is handed
/// bytes this deployment composed seconds earlier and wants them whole: markup is screened as it will be transmitted
/// rather than as a browser would be allowed to render it, because a credential inside an attribute a sanitizer strips
/// still leaves in the message.
/// </para>
/// <para>
/// It exists so the MIME library stays inside its adapter. Implementations reach no network and no database: they are
/// handed the bytes a composition produced or a draft stored, and reading them can therefore neither transmit anything
/// nor touch a remote flag.
/// </para>
/// <para>
/// A parse failure is not modelled, and that is a statement about where the bytes come from rather than an omission.
/// Every message reaching this port was composed by this deployment's own composer — a send from what an author wrote,
/// an occasion from a stored declaration, a draft from either — so bytes that will not parse are a defect in that
/// composer, and an implementation lets the failure travel as one.
/// </para>
/// </remarks>
public interface IOutgoingMailTextReader
{
    /// <summary>Reads the subject and the body representations out of one composed message.</summary>
    /// <param name="rawMime">The RFC 822 bytes that will be stored and transmitted.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    /// <returns>What the message says in words, with no attachment read and no refusal.</returns>
    /// <remarks>
    /// This is the cheap read and the one to reach for by default. It opens no attachment, so it reaches no document
    /// parser and costs the parse of the message's own structure — which is what a caller wanting to show an author
    /// their own draft back needs, and all it needs.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    Task<OutgoingMailText> ReadWordsAsync(ReadOnlyMemory<byte> rawMime, CancellationToken cancellationToken);

    /// <summary>Reads the same words plus the text of every document the message attaches.</summary>
    /// <param name="rawMime">The RFC 822 bytes that will be stored and transmitted.</param>
    /// <param name="cancellationToken">Cancels the parse and the extraction.</param>
    /// <returns>What the message says in words, or why its attachments left nothing to judge them by.</returns>
    /// <remarks>
    /// <para>
    /// Separate from the read above because it costs a different order of work: it opens each attachment and offers it
    /// to <see cref="Emails.Extraction.Attachments.IAttachmentTextExtractor" />, so a message of large documents runs
    /// document parsers on whatever thread the caller is holding. Only a caller that will judge what comes back asks
    /// for it, and a caller that only wants the words asks for the read above instead.
    /// </para>
    /// <para>
    /// An attachment nothing here recognizes as a document contributes nothing and refuses nothing, because no text
    /// scanner ever undertook to read one. Everything else that yields no text is a refusal, since an attachment that
    /// was not read must never be treated as clean.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    Task<OutgoingMailText> ReadForScreeningAsync(ReadOnlyMemory<byte> rawMime, CancellationToken cancellationToken);
}
