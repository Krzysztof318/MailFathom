// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Host.Configuration.Embeddings;

/// <summary>Declares which document attachments this deployment reads, and what reading one may consume.</summary>
/// <remarks>
/// <para>
/// It sits under <c>Embeddings</c> rather than beside the synchronization bounds because
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md">ADR 0029</see>
/// put the extraction ceilings there: what an attachment costs to parse is a different quantity from what a passage
/// costs to embed, and the record keeps the two beside each other rather than inside one another.
/// </para>
/// <para>
/// Every key here bounds one extraction. What a message and an account run may spend across their attachments is a
/// second budget the same record names, and it arrives with the pipeline that spends it.
/// </para>
/// </remarks>
internal sealed class AttachmentTextOptions : IValidatableObject
{
    /// <summary>Gets the formats an attachment is offered to a parser for.</summary>
    /// <remarks>
    /// Writing nothing reads every format MailFathom parses, and naming any narrows to exactly those. The list starts
    /// empty rather than pre-filled because the configuration binder adds to a collection it finds rather than
    /// replacing it, so a pre-filled default would leave an operator naming one format with that one and the four they
    /// were narrowing away from. Naming a format nothing here parses is refused at startup rather than ignored.
    /// </remarks>
    public IList<AttachmentDocumentFormat> Formats { get; } = [];

    /// <summary>Gets or sets the octets one attachment may hold before it is read at all.</summary>
    [Range(1024, 512L * 1024 * 1024)]
    public long MaxInputOctets { get; set; } = AttachmentTextExtractionOptions.DefaultMaxInputOctets;

    /// <summary>Gets or sets the characters one attachment may contribute.</summary>
    [Range(1_000, 10_000_000)]
    public int MaxExtractedTextCharacters { get; set; } = AttachmentTextExtractionOptions.DefaultMaxExtractedTextCharacters;

    /// <summary>Gets or sets the total octets a container format may decompress to.</summary>
    [Range(1024, 2L * 1024 * 1024 * 1024)]
    public long MaxDecompressedOctets { get; set; } = AttachmentTextExtractionOptions.DefaultMaxDecompressedOctets;

    /// <summary>Gets or sets the greatest ratio of decompressed to compressed octets one container part may reach.</summary>
    [Range(2, 10_000)]
    public int MaxDecompressionRatio { get; set; } = AttachmentTextExtractionOptions.DefaultMaxDecompressionRatio;

    /// <summary>Gets or sets the parts a container format may declare.</summary>
    [Range(1, 100_000)]
    public int MaxContainerParts { get; set; } = AttachmentTextExtractionOptions.DefaultMaxContainerParts;

    /// <summary>Gets or sets the depth an element tree inside a container part may nest to.</summary>
    [Range(2, 10_000)]
    public int MaxElementDepth { get; set; } = AttachmentTextExtractionOptions.DefaultMaxElementDepth;

    /// <summary>Gets or sets the time one extraction may take before it is abandoned.</summary>
    public TimeSpan Timeout { get; set; } = AttachmentTextExtractionOptions.DefaultTimeout;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.Timeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "Embeddings AttachmentText Timeout is a positive duration, because an unbounded extraction would hold "
                + "a document parser open over a sender's own bytes for as long as that parser kept working.",
                [nameof(this.Timeout)]);
        }

        foreach (var refused in this.Formats.Where(format => !AttachmentDocumentFormats.IsExtracted(format)))
        {
            yield return new ValidationResult(
                $"Embeddings AttachmentText Formats names '{refused}', which MailFathom recognizes and does not read. "
                + "Naming it would read as a deployment that extracts it.",
                [nameof(this.Formats)]);
        }
    }

    /// <summary>Reads the keys one extraction is bounded by.</summary>
    /// <returns>The bounds the port applies.</returns>
    internal AttachmentTextExtractionOptions ToExtractionOptions()
    {
        var extraction = new AttachmentTextExtractionOptions
        {
            MaxInputOctets = this.MaxInputOctets,
            MaxExtractedTextCharacters = this.MaxExtractedTextCharacters,
            MaxDecompressedOctets = this.MaxDecompressedOctets,
            MaxDecompressionRatio = this.MaxDecompressionRatio,
            MaxContainerParts = this.MaxContainerParts,
            MaxElementDepth = this.MaxElementDepth,
            Timeout = this.Timeout,
        };

        if (this.Formats.Count == 0)
        {
            return extraction;
        }

        extraction.Formats.Clear();

        foreach (var format in this.Formats)
        {
            extraction.Formats.Add(format);
        }

        return extraction;
    }
}
