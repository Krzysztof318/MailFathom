// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Host.Configuration.Embeddings;

/// <summary>Declares whether this deployment reads its mail's attachments at all, which of them, and what that may consume.</summary>
/// <remarks>
/// <para>
/// It sits under <c>Embeddings</c> rather than beside the synchronization bounds because
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md">ADR 0029</see>
/// put the extraction ceilings there: what an attachment costs to parse is a different quantity from what a passage
/// costs to embed, and the record keeps the two beside each other rather than inside one another.
/// </para>
/// <para>
/// The keys fall into three groups. <see cref="Enabled" /> is the switch, and with it off nothing on this block is
/// reached at all. <see cref="MaxAttachmentsPerEmail" />, <see cref="MaxInputOctetsPerEmail" />, and
/// <see cref="MaxInputOctetsPerAccountRun" /> are the second budget the same record names — what one message and one
/// account run may spend across their attachments — and they bound the pass that walks a mailbox rather than one
/// reading. Everything else here bounds one extraction, and is what the parser is handed.
/// </para>
/// </remarks>
internal sealed class AttachmentTextOptions : IValidatableObject
{
    /// <summary>The longest extraction deadline this block accepts.</summary>
    /// <remarks>
    /// An hour is far past any honest document and far below the platform timer's own maximum, which is what makes it
    /// the useful ceiling: a <c>TimeSpan</c> bound from <c>"30"</c> is thirty days rather than thirty seconds, and a
    /// deadline built from that would throw out of a port whose whole contract is that it answers instead.
    /// </remarks>
    private static readonly TimeSpan GreatestTimeout = TimeSpan.FromHours(1);

    /// <summary>Gets or sets whether this deployment reads its mail's attachments at all.</summary>
    /// <remarks>
    /// Off by default, which is the whole of what
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md">ADR 0029</see>
    /// decides about what an instance does when it upgrades into this: an embedding is derived from message text
    /// always, and from attachment text only where a deployment turns it on. Turning it on narrows the current
    /// undertaking to parse nothing a stranger composed rather than reversing it — what is read is the allow-list
    /// below and nothing else — and it means the most sensitive part of a mailbox starts travelling to whichever
    /// provider embeds it. That is a decision an operator takes rather than one a release takes for them.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how many of one message's attachments are read at all, counted in walk order.</summary>
    public int MaxAttachmentsPerEmail { get; set; } = EmailAttachmentTextBounds.DefaultMaxAttachmentsPerEmail;

    /// <summary>Gets or sets how many octets one message's attachments may be read from together.</summary>
    public long MaxInputOctetsPerEmail { get; set; } = EmailAttachmentTextBounds.DefaultMaxInputOctetsPerEmail;

    /// <summary>Gets or sets how many octets one account run may read across every message it reaches.</summary>
    public long MaxInputOctetsPerAccountRun { get; set; } =
        EmailAttachmentTextBounds.DefaultMaxInputOctetsPerAccountRun;

    /// <summary>Gets the formats an attachment is offered to a parser for.</summary>
    /// <remarks>
    /// Writing nothing reads every format MailFathom parses, and naming any narrows to exactly those. The list starts
    /// empty rather than pre-filled because the configuration binder adds to a collection it finds rather than
    /// replacing it, so a pre-filled default would leave an operator naming one format with that one and the nine
    /// they were narrowing away from. Naming a format nothing here parses is refused at startup rather than ignored.
    /// </remarks>
    public IList<AttachmentDocumentFormat> Formats { get; } = [];

    /// <summary>Gets or sets the octets one attachment may hold before it is read at all.</summary>
    public long MaxInputOctets { get; set; } = AttachmentTextExtractionOptions.DefaultMaxInputOctets;

    /// <summary>Gets or sets the characters one attachment may contribute.</summary>
    public int MaxExtractedTextCharacters { get; set; } = AttachmentTextExtractionOptions.DefaultMaxExtractedTextCharacters;

    /// <summary>Gets or sets the total octets a container format may decompress to.</summary>
    public long MaxDecompressedOctets { get; set; } = AttachmentTextExtractionOptions.DefaultMaxDecompressedOctets;

    /// <summary>Gets or sets the greatest ratio of decompressed to compressed octets one container part may reach.</summary>
    public int MaxDecompressionRatio { get; set; } = AttachmentTextExtractionOptions.DefaultMaxDecompressionRatio;

    /// <summary>Gets or sets the parts a container format may declare.</summary>
    public int MaxContainerParts { get; set; } = AttachmentTextExtractionOptions.DefaultMaxContainerParts;

    /// <summary>Gets or sets the depth an element tree inside a container part may nest to.</summary>
    public int MaxElementDepth { get; set; } = AttachmentTextExtractionOptions.DefaultMaxElementDepth;

    /// <summary>Gets or sets the time one extraction may take before it is abandoned.</summary>
    public TimeSpan Timeout { get; set; } = AttachmentTextExtractionOptions.DefaultTimeout;

    /// <inheritdoc />
    /// <remarks>
    /// Every range is checked here rather than declared as a <see cref="RangeAttribute" />, because this block is a
    /// complex property of the section the options framework validates and <c>Validator.TryValidateObject</c> does not
    /// descend into one — an attribute here would publish a constraint nothing applies, which is worse than none. The
    /// upper bounds are as much the point as the lower: a ceiling raised past what the process can carry removes the
    /// guard rather than widening it.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in this.FindCeilingErrors())
        {
            yield return error;
        }

        foreach (var error in this.FindCeilingOrderErrors())
        {
            yield return error;
        }

        foreach (var refused in this.Formats.Where(format => !AttachmentDocumentFormats.IsExtracted(format)))
        {
            yield return new ValidationResult(
                $"Embeddings AttachmentText Formats names '{refused}', which MailFathom recognizes and does not read. "
                + "Naming it would read as a deployment that extracts it.",
                [nameof(this.Formats)]);
        }
    }

    /// <summary>Reports every ceiling written outside the range it is meaningful in.</summary>
    private IEnumerable<ValidationResult> FindCeilingErrors()
    {
        if (this.Timeout <= TimeSpan.Zero || this.Timeout > GreatestTimeout)
        {
            yield return new ValidationResult(
                $"Embeddings AttachmentText Timeout is a positive duration no longer than {GreatestTimeout}. An "
                + "unbounded extraction would hold a document parser open over a sender's own bytes for as long as "
                + "that parser kept working, and a duration past this one is refused by the platform timer the "
                + "deadline is built on rather than applied.",
                [nameof(this.Timeout)]);
        }

        foreach (var (name, value, least, greatest) in this.Ceilings())
        {
            if (value < least || value > greatest)
            {
                yield return new ValidationResult(
                    $"Embeddings AttachmentText {name} is between {least} and {greatest}. A value outside that "
                    + "removes the guard the key exists to be rather than widening it.",
                    [name]);
            }
        }
    }

    /// <summary>Reports a set of ceilings written in an order that would leave a message no run could ever afford.</summary>
    /// <remarks>
    /// The three are nested rather than independent, and writing them otherwise produces a deployment that looks
    /// configured and reads nothing: an attachment larger than what its message may spend is refused by every run for
    /// ever, and a message larger than what its run may spend is passed over by every run for ever, since a run always
    /// starts with a full budget and always meets that message with less than it needs. Refused at startup rather than
    /// discovered as mail that is silently never searched.
    /// </remarks>
    private IEnumerable<ValidationResult> FindCeilingOrderErrors()
    {
        if (this.MaxInputOctetsPerEmail < this.MaxInputOctets)
        {
            yield return new ValidationResult(
                "Embeddings AttachmentText MaxInputOctetsPerEmail is at least MaxInputOctets. Below it, an "
                + "attachment this deployment says it may read is one no message may afford, so it would be recorded "
                + "as past a ceiling on every run and never read.",
                [nameof(this.MaxInputOctetsPerEmail)]);
        }

        if (this.MaxInputOctetsPerAccountRun < this.MaxInputOctetsPerEmail)
        {
            yield return new ValidationResult(
                "Embeddings AttachmentText MaxInputOctetsPerAccountRun is at least MaxInputOctetsPerEmail. Below it, "
                + "a message this deployment says it may read is one no run may afford, and since every run starts "
                + "with a full budget that message would be passed over for ever.",
                [nameof(this.MaxInputOctetsPerAccountRun)]);
        }
    }

    /// <summary>Reads the ceilings one message and one account run are read under.</summary>
    /// <returns>The bounds the pass applies.</returns>
    internal EmailAttachmentTextBounds ToAttachmentTextBounds() => new(
        this.Enabled,
        this.MaxAttachmentsPerEmail,
        this.MaxInputOctetsPerEmail,
        this.MaxInputOctetsPerAccountRun);

    /// <summary>Names every numeric ceiling beside the range it is meaningful in.</summary>
    private IEnumerable<(string Name, long Value, long Least, long Greatest)> Ceilings() =>
    [
        (nameof(this.MaxInputOctets), this.MaxInputOctets, 1024, 512L * 1024 * 1024),
        (nameof(this.MaxExtractedTextCharacters), this.MaxExtractedTextCharacters, 1_000, 10_000_000),
        (nameof(this.MaxDecompressedOctets), this.MaxDecompressedOctets, 1024, 2L * 1024 * 1024 * 1024),
        (nameof(this.MaxDecompressionRatio), this.MaxDecompressionRatio, 2, 10_000),
        (nameof(this.MaxContainerParts), this.MaxContainerParts, 1, 100_000),
        (nameof(this.MaxElementDepth), this.MaxElementDepth, 2, 10_000),
        (nameof(this.MaxAttachmentsPerEmail), this.MaxAttachmentsPerEmail, 1, 1_000),
        (nameof(this.MaxInputOctetsPerEmail), this.MaxInputOctetsPerEmail, 1024, 8L * 1024 * 1024 * 1024),
        (nameof(this.MaxInputOctetsPerAccountRun), this.MaxInputOctetsPerAccountRun, 1024, 1024L * 1024 * 1024 * 1024),
    ];

    /// <summary>Reads the keys one extraction is bounded by.</summary>
    /// <returns>The bounds the port applies.</returns>
    internal AttachmentTextExtractionOptions ToExtractionOptions() => new()
    {
        Formats = this.Formats.Count == 0 ? AttachmentDocumentFormats.Extracted : [.. this.Formats],
        MaxInputOctets = this.MaxInputOctets,
        MaxExtractedTextCharacters = this.MaxExtractedTextCharacters,
        MaxDecompressedOctets = this.MaxDecompressedOctets,
        MaxDecompressionRatio = this.MaxDecompressionRatio,
        MaxContainerParts = this.MaxContainerParts,
        MaxElementDepth = this.MaxElementDepth,
        Timeout = this.Timeout,
    };
}
