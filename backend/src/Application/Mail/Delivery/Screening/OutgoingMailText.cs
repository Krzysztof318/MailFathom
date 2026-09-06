// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Mail.Delivery.Screening;

/// <summary>Everything an outgoing message says in words, read back out of the bytes that will be transmitted.</summary>
/// <param name="Subject">The subject line, which is empty text where the message carries none.</param>
/// <param name="PlainTextBody">The plain-text body, which is empty text where the message carries none.</param>
/// <param name="HtmlBody">The HTML body, or <see langword="null" /> where the message carries none.</param>
/// <remarks>
/// <para>
/// <b>Three values rather than the message.</b> A scanner reports the region it matched, and a region found in a
/// composed document can cover a boundary as readily as the text beside it — so what is screened is each field on its
/// own, exactly as every consumer of the redacting guard screens the field it owns rather than the envelope it will
/// build. That the screen only ever answers yes or no makes this a matter of accuracy rather than of safety here, and
/// it is worth as much: a match straddling a MIME boundary is a match against something nobody wrote.
/// </para>
/// <para>
/// <b>The attachments are the one part of the count a message decides.</b> Everything the author typed is at most three
/// scans; each readable attachment adds one more, so what bounds the work is what bounds the extraction behind it —
/// the per-attachment ceilings <see cref="AttachmentTextExtractionOptions" /> declares, and the same numbers applied
/// across the message by whatever reads it back. A message that carries no attachment costs exactly what it cost
/// before.
/// </para>
/// <para>
/// <b>No header is among the three, and they are not all covered by the same thing.</b> A message identity is composed
/// by this deployment out of values it chose, so there is nothing in one a caller could have put there. An address is
/// judged beside this screen by <c>OutgoingRecipientPolicy</c>, which is what decides whether the message may go to
/// that person at all. <b>A display name is neither</b>: the policy reads the address alone, and the composer writes
/// the name into the header having checked it only for an embedded line break. Nothing published today lets a caller
/// state one — every recipient is an address, or a contact whose name this deployment holds — so this is a gap in what
/// is guaranteed rather than a value going out unexamined. An entrypoint that does let a caller name a recipient in
/// their own words is what would make it one, and screening the header is what it would owe.
/// </para>
/// <para>
/// <b>An attachment is read as far as something here can read it, and a file that could not be read stops the act.</b>
/// A credential typed into a covering note and the same credential inside the attached document leave under the same
/// address, so screening one and not the other made the protection a property of which half of the message the author
/// put it in. What is not undertaken is anything that is not a document: a photograph, a recording, and an archive
/// carry no text a scanner reads, so they are neither read nor counted, and the page beside this says so rather than
/// implying coverage.
/// </para>
/// </remarks>
public sealed record OutgoingMailText(string Subject, string PlainTextBody, string? HtmlBody)
{
    /// <summary>Gets the text each of the message's readable attachments yielded, in the order the message carries them.</summary>
    /// <remarks>
    /// Each attachment is one entry and one scan, for the reason the three values above are separate: a region matched
    /// across the join between two files is a match against something nobody attached. A document that yielded no
    /// characters at all — every page of it a scan of paper — contributes an empty entry that the screened values drop,
    /// which is the same answer an image gets and for the same reason.
    /// </remarks>
    public IReadOnlyList<string> AttachmentTexts { get; init; } = [];

    /// <summary>Gets why one attachment could not be read, or <see langword="null" /> where every document the message carries was read.</summary>
    /// <remarks>
    /// <para>
    /// It carries the outcome rather than a flag so that whatever reads this back can be tested against the reason it
    /// stopped, and it is deliberately not carried any further than that: the refusal a caller is told names none of
    /// these, because which shape of unreadable file a deployment stops at is a fact about the screen rather than about
    /// the message.
    /// </para>
    /// <para>
    /// <see cref="AttachmentTextExtractionOutcome.FormatNotRecognized" /> is never what this holds. An attachment
    /// nothing recognized as a document is not a document this deployment failed to read; it is a file no reader here
    /// ever undertook to read, and stopping a send over one would refuse every message carrying a photograph.
    /// </para>
    /// </remarks>
    public AttachmentTextExtractionOutcome? UnreadableAttachment { get; init; }

    /// <summary>Gets the values to screen, in the order they are scanned and with what the message does not carry left out.</summary>
    /// <remarks>
    /// The subject comes first because it is the shortest and therefore the cheapest way for a message to be refused,
    /// and the screen stops at the first value that refuses. The attachments come last for the same reason read the
    /// other way: they are the longest and the ones a message may carry several of. Empty text is dropped rather than
    /// scanned: it can carry nothing, and scanning it would spend one analyzer round trip per message that has no HTML
    /// alternative.
    /// </remarks>
    public IReadOnlyList<string> ScreenedValues =>
    [
        .. new[] { this.Subject, this.PlainTextBody, this.HtmlBody }
            .Concat(this.AttachmentTexts)
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!),
    ];
}
