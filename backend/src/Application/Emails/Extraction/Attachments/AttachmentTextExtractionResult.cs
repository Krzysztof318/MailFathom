// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Attachments;

/// <summary>States what reading one attachment produced, or exactly what stopped it.</summary>
/// <remarks>
/// The set is closed, and every member is distinguishable from every other, because the whole point of the reasons is
/// that a mailbox owner is told their contract was skipped rather than searched and found empty. Nothing here collapses
/// into a generic failure and nothing is represented by an empty string.
/// </remarks>
public enum AttachmentTextExtractionOutcome
{
    /// <summary>The attachment was read and its text is present.</summary>
    Extracted = 0,

    /// <summary>Neither the media type nor the file name names a document format, so nothing was offered a parser.</summary>
    FormatNotRecognized = 1,

    /// <summary>The format is recognized and nothing here parses it.</summary>
    /// <remarks>What the three legacy binary formats produce, and what a format an operator excluded produces.</remarks>
    FormatNotExtracted = 2,

    /// <summary>The attachment holds more octets than the configured input ceiling, so it was not read.</summary>
    InputTooLarge = 3,

    /// <summary>The attachment yielded more characters than the configured output ceiling, so it was abandoned.</summary>
    ExtractedTextTooLarge = 4,

    /// <summary>A container format exceeded a decompression, ratio, part-count, or nesting ceiling while it was being read.</summary>
    ContainerBoundExceeded = 5,

    /// <summary>The document is encrypted and no password to it exists anywhere in this system.</summary>
    Encrypted = 6,

    /// <summary>The bytes do not parse as the format they declare, which is expected of real mail rather than exceptional.</summary>
    Malformed = 7,

    /// <summary>Reading took longer than the configured timeout, so it was abandoned.</summary>
    TimedOut = 8,
}

/// <summary>Carries the text one attachment yielded, or the reason it yielded none.</summary>
/// <remarks>
/// Failure is a result rather than an exception for the reason MIME extraction gives: an attachment nobody can read
/// must be recorded and stepped over, leaving the message and the run that found it to continue. A parser raising
/// something a caller would have to interpret is exactly what this port exists to stop.
/// </remarks>
public sealed record AttachmentTextExtractionResult
{
    private AttachmentTextExtractionResult(AttachmentTextExtractionOutcome outcome, ExtractedAttachmentText? text)
    {
        this.Outcome = outcome;
        this.Text = text;
    }

    /// <summary>Gets what happened.</summary>
    public AttachmentTextExtractionOutcome Outcome { get; }

    /// <summary>Gets the extracted text, which is present exactly when <see cref="Outcome" /> is <see cref="AttachmentTextExtractionOutcome.Extracted" />.</summary>
    /// <remarks>
    /// A document every page of which yielded nothing is still an extraction: the text is empty, and
    /// <see cref="ExtractedAttachmentText.PagesWithoutText" /> names every page, which is what separates a scan from a
    /// failure.
    /// </remarks>
    public ExtractedAttachmentText? Text { get; }

    /// <summary>Reports an attachment that was read.</summary>
    /// <param name="text">What it yielded.</param>
    /// <returns>An extracted result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    public static AttachmentTextExtractionResult Extracted(ExtractedAttachmentText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new AttachmentTextExtractionResult(AttachmentTextExtractionOutcome.Extracted, text);
    }

    /// <summary>Reports an attachment whose declaration names no document format.</summary>
    /// <returns>An unrecognized result.</returns>
    public static AttachmentTextExtractionResult FormatNotRecognized() =>
        new(AttachmentTextExtractionOutcome.FormatNotRecognized, text: null);

    /// <summary>Reports a recognized format nothing here reads.</summary>
    /// <returns>An unparsed result.</returns>
    public static AttachmentTextExtractionResult FormatNotExtracted() =>
        new(AttachmentTextExtractionOutcome.FormatNotExtracted, text: null);

    /// <summary>Reports an attachment past the configured input ceiling.</summary>
    /// <returns>An oversized-input result.</returns>
    public static AttachmentTextExtractionResult InputTooLarge() =>
        new(AttachmentTextExtractionOutcome.InputTooLarge, text: null);

    /// <summary>Reports an attachment that yielded more text than the configured output ceiling.</summary>
    /// <returns>An oversized-output result.</returns>
    public static AttachmentTextExtractionResult ExtractedTextTooLarge() =>
        new(AttachmentTextExtractionOutcome.ExtractedTextTooLarge, text: null);

    /// <summary>Reports a container that exceeded one of its structural ceilings.</summary>
    /// <returns>A container-bound result.</returns>
    public static AttachmentTextExtractionResult ContainerBoundExceeded() =>
        new(AttachmentTextExtractionOutcome.ContainerBoundExceeded, text: null);

    /// <summary>Reports a document this system holds no password for.</summary>
    /// <returns>An encrypted result.</returns>
    public static AttachmentTextExtractionResult Encrypted() =>
        new(AttachmentTextExtractionOutcome.Encrypted, text: null);

    /// <summary>Reports bytes that do not parse as the format they declare.</summary>
    /// <returns>A malformed result.</returns>
    public static AttachmentTextExtractionResult Malformed() =>
        new(AttachmentTextExtractionOutcome.Malformed, text: null);

    /// <summary>Reports an extraction abandoned at its timeout.</summary>
    /// <returns>A timed-out result.</returns>
    public static AttachmentTextExtractionResult TimedOut() =>
        new(AttachmentTextExtractionOutcome.TimedOut, text: null);
}
