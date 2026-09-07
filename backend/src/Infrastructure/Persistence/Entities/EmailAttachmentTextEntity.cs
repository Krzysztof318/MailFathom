// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.CodeCoverage;
using NpgsqlTypes;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>What one attachment of a stored email yielded as words, and the lexical index built over the ones somebody wrote.</summary>
/// <remarks>
/// <para>
/// A row per attachment whichever way the reading went, because the reason an attachment yielded nothing is a durable
/// fact a user is owed and a record that stops the next run from offering the same file to a parser again. Keeping
/// the words here rather than only in the passages cut from them is what makes re-cutting a mailbox local: a boundary
/// rule tuned upwards re-reads this text and re-embeds only the passages that actually changed, instead of parsing
/// every document and calling a vision model a second time.
/// </para>
/// <para>
/// <b>The lexical index lives on this row rather than on the message's own search document</b>, which is the shape
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md">ADR 0029</see>
/// was amended to. Appending a file's words to the message's document would make them match as though the message had
/// contained them, and <c>ts_headline</c> would quote a contract inside the snippet shown for the covering note. A
/// document of its own also carries what a hit has to name: which attachment matched, and — through the passages cut
/// from the same text — where inside it.
/// </para>
/// <para>
/// <b>A description is embedded and never indexed lexically.</b> The generated vector is built only for a row whose
/// kind is a document, which is
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>'s
/// ruling expressed where no writer can forget it: searching for a word by the letters it is written in must not return
/// a picture in which a model guessed it.
/// </para>
/// <para>
/// The words and the file name are mail content and inherit the message's classification, retention, export, and
/// erasure obligations whole. The cascade from the stored email is what keeps a deletion of the message a deletion of
/// everything derived from its attachments.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailAttachmentTextEntity
{
    /// <summary>The greatest number of file-name characters a row stores.</summary>
    /// <remarks>
    /// A file name is text a sender chose and MIME bounds it nowhere. The stored value is what the attachment strip and
    /// a citation show, so it is bounded here rather than allowed to decide how wide the row is.
    /// </remarks>
    internal const int MaximumFileNameLength = 512;

    /// <summary>The greatest number of characters an outcome name occupies.</summary>
    internal const int MaximumOutcomeLength = 64;

    /// <summary>The greatest number of characters a declared media type occupies.</summary>
    internal const int MaximumMediaTypeLength = 255;

    public Guid StoredEmailId { get; set; }

    public required StoredEmailEntity StoredEmail { get; set; }

    /// <summary>Gets or sets the zero-based place the attachment holds in the order the message's structure is walked.</summary>
    /// <remarks>
    /// The identity, together with the message, because it is the only stable one a message's parts have: a file name
    /// is neither unique nor required, and this is the coordinate the download route is addressed with and the one a
    /// citation resolves.
    /// </remarks>
    public int AttachmentPosition { get; set; }

    /// <summary>Gets or sets whether the words are the file's own or a model's account of a picture.</summary>
    public AttachmentTextKind Kind { get; set; }

    /// <summary>Gets or sets the media type the part declared, which is what decided the parser its words came from.</summary>
    public required string DeclaredMediaType { get; set; }

    /// <summary>Gets or sets the normalized file name, or <see langword="null" /> where the part carried no usable name.</summary>
    public string? FileName { get; set; }

    /// <summary>Gets or sets what happened, named by the member of the closed set the port that answered publishes.</summary>
    public required string Outcome { get; set; }

    /// <summary>Gets or sets the words the attachment yielded, or <see langword="null" /> where it yielded none.</summary>
    /// <remarks>
    /// Unbounded for the reason a passage is: what bounds it is the per-attachment output ceiling extraction already
    /// applied, and a column bound would add a write failure the first time that ceiling was raised.
    /// </remarks>
    public string? Text { get; set; }

    /// <summary>Gets or sets how many pages, slides, or sheets the document was read as, which is zero for anything unread.</summary>
    public int PageCount { get; set; }

    /// <summary>Gets or sets where each of those pages begins in <see cref="Text" />, as the JSON document the segments serialize to.</summary>
    /// <remarks>
    /// A document rather than a table of its own, because nothing queries a boundary: the whole list is read together
    /// with the text it indexes, by the one reader that turns a passage's offset into a place. A child table would add
    /// a join and an ordering to every such read and answer no question the list does not.
    /// </remarks>
    public string? Segments { get; set; }

    /// <summary>Gets or sets when this reading was taken, which tells a re-derivation from an original one apart.</summary>
    public DateTimeOffset DerivedAt { get; set; }

    /// <summary>
    /// Gets or sets the sensitive-content configuration these words were written under, or <see langword="null" />
    /// when they were derived with no scanner switched on.
    /// </summary>
    /// <remarks>
    /// Its own stamp rather than the message's, because an attachment is read on a later pass than the body and a
    /// posture republished between the two would otherwise leave one row's stamp standing for text the other never went
    /// through. The startup report counts the rows whose stamp is not their user's current one, and a rebuilding
    /// extraction backfill discards them and takes the message's reading marker off, which puts it back in front of the
    /// attachment stage — so the words are taken again under that run's own budgets rather than by the walk that
    /// discarded them.
    /// </remarks>
    public string? SensitiveContentStamp { get; set; }

    /// <summary>Gets or sets the search vector PostgreSQL generates from the words a person actually wrote.</summary>
    /// <remarks>
    /// Never assigned by MailFathom, and null for a row whose kind is a description. The column is
    /// <c>GENERATED ALWAYS ... STORED</c>, so PostgreSQL recomputes it from this row on every insert and update and no
    /// code path can leave it disagreeing with the text beside it — nor index a description by writing one.
    /// </remarks>
    public NpgsqlTsVector? SearchVector { get; set; }
}
