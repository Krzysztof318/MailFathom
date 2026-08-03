// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>States whether a reader was given the message body, or why it could not be.</summary>
/// <remarks>
/// The two unreadable cases stay apart because a caller acts on them differently and neither is an empty message. One
/// is mail this deployment holds and cannot decrypt, the other is mail whose bytes were deliberately never stored, and
/// only the second can be changed by configuration.
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
        EmailBodyRepresentation? sanitizedHtml)
    {
        this.Availability = availability;
        this.PlainText = plainText;
        this.SanitizedHtml = sanitizedHtml;
    }

    /// <summary>Gets whether the body could be read, or why it could not.</summary>
    public EmailBodyAvailability Availability { get; }

    /// <summary>Gets the plain-text representation, which is empty whenever the body could not be read.</summary>
    public EmailBodyRepresentation PlainText { get; }

    /// <summary>Gets the sanitized HTML representation, or <see langword="null" /> when none was produced.</summary>
    public EmailBodyRepresentation? SanitizedHtml { get; }

    /// <summary>Gets the body of a message whose own body arrived encrypted.</summary>
    public static EmailContentBody EncryptedNotReadableLocally { get; } = new(
        EmailBodyAvailability.EncryptedNotReadableLocally,
        EmailBodyRepresentation.Empty,
        sanitizedHtml: null);

    /// <summary>Gets the body of a message whose raw MIME exceeded the size limit and was never stored.</summary>
    public static EmailContentBody NotStoredExceededSizeLimit { get; } = new(
        EmailBodyAvailability.NotStoredExceededSizeLimit,
        EmailBodyRepresentation.Empty,
        sanitizedHtml: null);

    /// <summary>Reports a body that was read from the stored message.</summary>
    /// <param name="plainText">The plain-text representation, bounded and carrying its truncation metadata.</param>
    /// <param name="sanitizedHtml">The sanitized HTML representation, or <see langword="null" /> when none was produced.</param>
    /// <returns>The readable body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plainText" /> is <see langword="null" />.</exception>
    public static EmailContentBody Readable(
        EmailBodyRepresentation plainText,
        EmailBodyRepresentation? sanitizedHtml)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        return new EmailContentBody(EmailBodyAvailability.Readable, plainText, sanitizedHtml);
    }
}
