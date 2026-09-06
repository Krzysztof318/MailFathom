// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Extraction.Images;
using MailFathom.Application.SensitiveContent.Redaction;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>What one attachment of one message yielded, or the recorded reason it yielded nothing.</summary>
/// <remarks>
/// <para>
/// One of these is written per attachment whichever way it went, which is what makes a mailbox owner's question
/// answerable: a contract that was skipped says so, rather than being searched and found empty. It is also what stops a
/// retry loop — an attachment nothing could read has a durable record saying why, and the pass that walks the mailbox
/// steps past it instead of offering it to a parser on every run.
/// </para>
/// <para>
/// <see cref="Text" /> is mail content and is never a log line, a metric dimension, a trace attribute, or part of an
/// error message. <see cref="FileName" /> is mail content too — a sender chose it. The position, the media type, the
/// outcome, the page count, and the segment boundaries are the members safe to report.
/// </para>
/// </remarks>
public sealed record DerivedAttachmentText
{
    /// <summary>Names a description that was produced, in the vocabulary the refusals are named in.</summary>
    /// <remarks>
    /// <see cref="ImageAttachmentDescription" /> publishes the refusals as a closed set and success as the absence of
    /// one, so the success case needs a word of its own here. It matches
    /// <see cref="AttachmentTextExtractionOutcome.Extracted" />'s role on the other path.
    /// </remarks>
    private const string DescribedOutcome = "Described";

    /// <summary>Names an attachment the message's own octet or count ceiling stopped, which neither port publishes.</summary>
    /// <remarks>
    /// It belongs to <see cref="EmailAttachmentTextBounds" /> rather than to either extraction port, which is why it is
    /// a word here rather than a tenth member of a set that describes what a parser did.
    /// </remarks>
    private const string MessageBudgetOutcome = "MessageBudgetExhausted";

    /// <summary>The greatest number of characters a declared media type is recorded with.</summary>
    /// <remarks>
    /// The sender writes the <c>Content-Type</c> header and the MIME parser reads a type out of it with no ceiling of
    /// its own, so this is the one sender-controlled string on this record that nothing upstream bounds — a file name
    /// reaches here already normalized to a ceiling of its own. Without it a message declaring a three-hundred-character
    /// type makes the insert fail on the column's width, which is not a conflict the commit policy retries, so the
    /// message is never stamped and every later run reaches it first and fails at the same statement. RFC 6838 puts a
    /// real type and subtype far below this, so a claim longer than it is malformed rather than merely long.
    /// </remarks>
    public const int MaximumDeclaredMediaTypeLength = 255;

    private DerivedAttachmentText(
        int position,
        AttachmentTextKind kind,
        string declaredMediaType,
        string? fileName,
        string outcome,
        string? text,
        int pageCount,
        IReadOnlyList<AttachmentTextSegment> segments)
    {
        this.Position = position;
        this.Kind = kind;
        this.DeclaredMediaType = Bounded(declaredMediaType);
        this.FileName = fileName;
        this.Outcome = outcome;
        this.Text = text;
        this.PageCount = pageCount;
        this.Segments = segments;
    }

    /// <summary>Gets the zero-based place the attachment holds in the order the message's structure is walked.</summary>
    public int Position { get; }

    /// <summary>Gets whether these words are the file's own or a description of a picture.</summary>
    public AttachmentTextKind Kind { get; }

    /// <summary>Gets the media type the part declared, which is the sender's claim rather than a reading of the octets.</summary>
    public string DeclaredMediaType { get; }

    /// <summary>Gets the normalized file name, or <see langword="null" /> where the part carried no usable name.</summary>
    public string? FileName { get; }

    /// <summary>Gets what happened, named by the member of the closed set the port it came from publishes.</summary>
    /// <remarks>
    /// Text rather than an enumeration of its own, because the two ports already publish closed sets and neither shares
    /// a member name with the other: an extraction reports <c>Extracted</c> or one of its eight reasons, and a
    /// description reports <c>Described</c> or one of its nine. Reading <see cref="Kind" /> beside it says which set
    /// the word belongs to, and a third enumeration copying both would be one more place for a member to go missing.
    /// </remarks>
    public string Outcome { get; }

    /// <summary>Gets the words the attachment yielded, or <see langword="null" /> where it yielded none.</summary>
    public string? Text { get; }

    /// <summary>Gets how many pages, slides, or sheets the document was read as, which is zero for anything unread.</summary>
    public int PageCount { get; }

    /// <summary>Gets where each of those pages begins in <see cref="Text" />, empty for anything unread.</summary>
    public IReadOnlyList<AttachmentTextSegment> Segments { get; }

    /// <summary>Gets whether this attachment contributed words to derive passages from.</summary>
    public bool HasText => !string.IsNullOrEmpty(this.Text);

    /// <summary>Gets whether these words belong in the lexical index as well as in the vector one.</summary>
    /// <remarks>
    /// The one place <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>'s
    /// exclusion is decided, so no writer has to remember it.
    /// </remarks>
    public bool BelongsInLexicalIndex => this.HasText && this.Kind == AttachmentTextKind.Document;

    /// <summary>Reports the same derivation with its words replaced by their redacted form.</summary>
    /// <param name="redacted">The redaction the owner's switched-on scanner produced from these words.</param>
    /// <returns>The redacted derivation, or this one unchanged when it carries no words.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="redacted" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The segment boundaries are offsets into the words, and a placeholder is rarely the length of what it replaced,
    /// so every boundary past the first finding has moved. Each is therefore carried across rather than compared:
    /// <see cref="RedactedText.MapOffset" /> is the one thing that knows where a character went, and without it a
    /// two-hundred-page contract would lose every page boundary because a signature block held an email address. A
    /// boundary the analyzed ceiling dropped is left out, since the text it pointed into is not in the result — a
    /// citation that says nothing is better than one that sends a reader to the wrong page.
    /// </remarks>
    public DerivedAttachmentText WithRedactedText(RedactedText redacted)
    {
        ArgumentNullException.ThrowIfNull(redacted);

        if (this.Text is null)
        {
            return this;
        }

        return new DerivedAttachmentText(
            this.Position,
            this.Kind,
            this.DeclaredMediaType,
            this.FileName,
            this.Outcome,
            redacted.Text,
            this.PageCount,
            [
                .. this.Segments
                    .Select(segment => (segment, mapped: redacted.MapOffset(segment.StartOffset)))
                    .Where(carried => carried.mapped is not null)
                    .Select(carried => carried.segment with { StartOffset = carried.mapped!.Value }),
            ]);
    }

    /// <summary>Records an attachment the message's own ceiling stopped before anything was offered a parser.</summary>
    /// <param name="position">The attachment's walk position.</param>
    /// <param name="declaredMediaType">The media type the part declared.</param>
    /// <param name="fileName">The normalized file name, or <see langword="null" />.</param>
    /// <returns>The derivation to store.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaredMediaType" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Written rather than omitted, because
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md">ADR 0029</see>
    /// records an attachment past a ceiling as having yielded none rather than leaving it absent: an owner asking why
    /// their contract was not searched is owed the ceiling as an answer. <see cref="Kind" /> is
    /// <see cref="AttachmentTextKind.Document" /> because nothing here opened the file far enough to know what it is,
    /// and the value decides nothing for a row carrying no words — neither index holds one either way.
    /// </remarks>
    public static DerivedAttachmentText PastMessageBudget(
        int position,
        string declaredMediaType,
        string? fileName)
    {
        ArgumentNullException.ThrowIfNull(declaredMediaType);

        return new DerivedAttachmentText(
            position,
            AttachmentTextKind.Document,
            declaredMediaType,
            fileName,
            MessageBudgetOutcome,
            text: null,
            pageCount: 0,
            segments: []);
    }

    /// <summary>Records what reading one document attachment produced.</summary>
    /// <param name="position">The attachment's walk position.</param>
    /// <param name="declaredMediaType">The media type the part declared.</param>
    /// <param name="fileName">The normalized file name, or <see langword="null" />.</param>
    /// <param name="result">What the extractor answered.</param>
    /// <returns>The derivation to store.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaredMediaType" /> or <paramref name="result" /> is <see langword="null" />.</exception>
    public static DerivedAttachmentText FromExtraction(
        int position,
        string declaredMediaType,
        string? fileName,
        AttachmentTextExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(declaredMediaType);
        ArgumentNullException.ThrowIfNull(result);

        return new DerivedAttachmentText(
            position,
            AttachmentTextKind.Document,
            declaredMediaType,
            fileName,
            result.Outcome.ToString(),
            result.Text?.Text,
            result.Text?.PageCount ?? 0,
            result.Text?.Segments ?? []);
    }

    /// <summary>Records what describing one image attachment produced.</summary>
    /// <param name="position">The attachment's walk position.</param>
    /// <param name="declaredMediaType">The media type the part declared.</param>
    /// <param name="fileName">The normalized file name, or <see langword="null" />.</param>
    /// <param name="description">What the describer answered.</param>
    /// <returns>The derivation to store.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaredMediaType" /> or <paramref name="description" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A description is one place with no pagination to record, so it carries a single segment covering the whole of
    /// it. Without one a passage cut from a long description would resolve to no place at all, and a citation would
    /// have to special-case the kind rather than reading the same list every other attachment carries.
    /// </remarks>
    public static DerivedAttachmentText FromDescription(
        int position,
        string declaredMediaType,
        string? fileName,
        ImageAttachmentDescription description)
    {
        ArgumentNullException.ThrowIfNull(declaredMediaType);
        ArgumentNullException.ThrowIfNull(description);

        var described = description.Text is not null;

        return new DerivedAttachmentText(
            position,
            AttachmentTextKind.ImageDescription,
            declaredMediaType,
            fileName,
            description.Refusal is { } refusal ? refusal.ToString() : DescribedOutcome,
            description.Text,
            described ? 1 : 0,
            described
                ? [new AttachmentTextSegment(AttachmentTextSegmentKind.Page, Number: 1, Label: null, StartOffset: 0)]
                : []);
    }

    /// <summary>Holds a declared media type to the length it is stored at, without leaving half a character behind.</summary>
    /// <remarks>
    /// The trailing high surrogate is dropped because a cut through a surrogate pair leaves a lone code unit that no
    /// UTF-8 encoder can write, which would fail the same insert this ceiling exists to keep from failing.
    /// </remarks>
    private static string Bounded(string declaredMediaType)
    {
        if (declaredMediaType.Length <= MaximumDeclaredMediaTypeLength)
        {
            return declaredMediaType;
        }

        var kept = declaredMediaType.AsSpan(0, MaximumDeclaredMediaTypeLength);

        return char.IsHighSurrogate(kept[^1]) ? new string(kept[..^1]) : new string(kept);
    }
}
