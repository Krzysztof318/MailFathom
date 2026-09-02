// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>States whether a reader was given the message body, or why it could not be.</summary>
/// <remarks>
/// The three unreadable cases stay apart because a caller acts on each differently and none of them is an empty
/// message. One is mail this deployment holds and cannot decrypt; one is mail whose bytes a configured limit will
/// refuse on every run, so asking again is pointless; and one is mail whose bytes are simply not stored yet, so asking
/// again once storage has room is exactly the right thing to do.
/// </remarks>
public enum EmailBodyAvailability
{
    /// <summary>The body was read from the stored message, and an empty one means the message displayed nothing.</summary>
    Readable = 0,

    /// <summary>The body arrived inside a cryptographic envelope, so nothing here can read it.</summary>
    /// <remarks>Decryption is out of scope; the state exists so an unreadable body is explicit rather than an empty one.</remarks>
    EncryptedNotReadableLocally = 1,

    /// <summary>The message exceeded the configured raw MIME size limit, so its content was never stored locally.</summary>
    /// <remarks>
    /// This is not a defect and schedules no repair: synchronization recorded the occurrence and deliberately stored no
    /// content for it, and requesting repair would ask a later run to store what the same limit will refuse again.
    /// </remarks>
    NotStoredExceededSizeLimit = 2,

    /// <summary>Local content storage was at its configured ceiling when the message arrived, so its content is not stored yet.</summary>
    /// <remarks>
    /// This is not a defect and schedules no repair either, and unlike the state above it is temporary: a later
    /// synchronization run fetches the content as soon as the ceiling has room, and the same read then returns the body.
    /// </remarks>
    NotStoredAwaitingStorageHeadroom = 3,
}

/// <summary>Carries the body representations a reader receives, or the reason there are none.</summary>
/// <remarks>
/// The plain text is the default representation and is always present, empty in each of the states where nothing could
/// be read. The sanitized HTML is present only when the caller asked for it and the message actually has an HTML body
/// part, so its absence answers whichever of those two questions the caller was in a position to ask.
/// </remarks>
public sealed record EmailContentBody
{
    private EmailContentBody(
        EmailBodyAvailability availability,
        EmailBodyRepresentation plainText,
        EmailBodyRepresentation? sanitizedHtml,
        MailDocument? document,
        EmailBodyForms forms)
    {
        this.Availability = availability;
        this.PlainText = plainText;
        this.SanitizedHtml = sanitizedHtml;
        this.Document = document;
        this.Forms = forms;
    }

    /// <summary>Gets whether the body could be read, or why it could not.</summary>
    public EmailBodyAvailability Availability { get; }

    /// <summary>Gets which forms of its own body the message carried, whatever the caller asked to be produced from them.</summary>
    /// <remarks>
    /// It is what the representations above cannot say. The plain text is present for every readable body, derived from
    /// the markup where the sender wrote no text part, so a caller reading it back learns nothing about what arrived —
    /// and a caller choosing between the words and a richer rendering is asking exactly that. A body nothing parsed
    /// carries <see cref="EmailBodyForms.None" />, and the availability beside it says why.
    /// </remarks>
    public EmailBodyForms Forms { get; }

    /// <summary>Gets the plain-text representation, which is empty whenever the body could not be read.</summary>
    public EmailBodyRepresentation PlainText { get; }

    /// <summary>Gets the sanitized HTML representation, or <see langword="null" /> when none was produced.</summary>
    public EmailBodyRepresentation? SanitizedHtml { get; }

    /// <summary>Gets the body reduced to the document tree a reading pane draws, or <see langword="null" /> when none was produced.</summary>
    /// <remarks>
    /// A third representation rather than a reading of the second: it is produced from the same parse and it carries no
    /// markup at all, which is what lets a client render a message without an HTML parser and without an engine. It is
    /// absent unless a caller asked for it, so the two representations a model reads are unchanged by its existence.
    /// </remarks>
    public MailDocument? Document { get; }

    /// <summary>Gets the body of a message whose own body arrived encrypted.</summary>
    public static EmailContentBody EncryptedNotReadableLocally { get; } = new(
        EmailBodyAvailability.EncryptedNotReadableLocally,
        EmailBodyRepresentation.Empty,
        sanitizedHtml: null,
        document: null,
        EmailBodyForms.None);

    /// <summary>Gets the body of a message whose raw MIME exceeded the size limit and was never stored.</summary>
    public static EmailContentBody NotStoredExceededSizeLimit { get; } = new(
        EmailBodyAvailability.NotStoredExceededSizeLimit,
        EmailBodyRepresentation.Empty,
        sanitizedHtml: null,
        document: null,
        EmailBodyForms.None);

    /// <summary>Gets the body of a message whose content storage had no room for it yet.</summary>
    public static EmailContentBody NotStoredAwaitingStorageHeadroom { get; } = new(
        EmailBodyAvailability.NotStoredAwaitingStorageHeadroom,
        EmailBodyRepresentation.Empty,
        sanitizedHtml: null,
        document: null,
        EmailBodyForms.None);

    /// <summary>Reports a body that was read from the stored message.</summary>
    /// <param name="plainText">The plain-text representation, bounded and carrying its truncation metadata.</param>
    /// <param name="sanitizedHtml">The sanitized HTML representation, or <see langword="null" /> when none was produced.</param>
    /// <param name="document">The reduced document tree, or <see langword="null" /> when none was produced.</param>
    /// <param name="forms">Which forms of its own body the message carried.</param>
    /// <returns>The readable body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plainText" /> or <paramref name="forms" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The forms are stated by every caller rather than defaulted, because the one honest default is
    /// <see cref="EmailBodyForms.None" /> and a body that was read carried something: a caller that forgot would publish
    /// a message as carrying neither form, which reads as a finding about the message instead of as an omission.
    /// </remarks>
    public static EmailContentBody Readable(
        EmailBodyRepresentation plainText,
        EmailBodyRepresentation? sanitizedHtml,
        MailDocument? document,
        EmailBodyForms forms)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        ArgumentNullException.ThrowIfNull(forms);

        return new EmailContentBody(EmailBodyAvailability.Readable, plainText, sanitizedHtml, document, forms);
    }
}
