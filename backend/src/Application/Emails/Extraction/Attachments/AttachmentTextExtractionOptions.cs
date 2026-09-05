// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Attachments;

/// <summary>Bounds what reading one attachment's text may consume, and which formats are offered to a parser at all.</summary>
/// <remarks>
/// <para>
/// Every value here is a ceiling rather than a target, and each one exists because a sender chose the bytes. A document
/// parser is the largest attack surface this system has: an archive with a small compressed size and an enormous
/// declared one, an element tree nested until a walk runs out of stack, and a page that decodes into more text than a
/// mailbox holds in a year are all ordinary shapes of a mail-borne attack rather than corner cases.
/// </para>
/// <para>
/// Exceeding any of them abandons the attachment and says which ceiling stopped it. Nothing is truncated into a partial
/// answer, because a partial extract presented as an extract is indistinguishable from a document that really said
/// only that much.
/// </para>
/// </remarks>
public sealed class AttachmentTextExtractionOptions
{
    /// <summary>The octets one attachment may hold where a deployment declares no ceiling of its own.</summary>
    public const long DefaultMaxInputOctets = 16L * 1024 * 1024;

    /// <summary>The characters one attachment may contribute where a deployment declares no ceiling of its own.</summary>
    public const int DefaultMaxExtractedTextCharacters = 200_000;

    /// <summary>The octets a container may inflate to where a deployment declares no ceiling of its own.</summary>
    public const long DefaultMaxDecompressedOctets = 64L * 1024 * 1024;

    /// <summary>The inflation ratio one container part may reach where a deployment declares no ceiling of its own.</summary>
    public const int DefaultMaxDecompressionRatio = 200;

    /// <summary>The parts a container may declare where a deployment declares no ceiling of its own.</summary>
    public const int DefaultMaxContainerParts = 2_000;

    /// <summary>The depth an element tree may nest to where a deployment declares no ceiling of its own.</summary>
    public const int DefaultMaxElementDepth = 100;

    /// <summary>The time one extraction may take where a deployment declares no ceiling of its own.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the formats an attachment is offered to a parser for.</summary>
    /// <remarks>
    /// The undertaking rather than whatever arrives: an attachment whose recognized format is absent from this list is
    /// skipped without a parser ever seeing its bytes, which is what lets a deployment narrow the surface it accepts
    /// without narrowing what MailFathom is able to read. It defaults to every format a parser here reads.
    /// </remarks>
    public IList<AttachmentDocumentFormat> Formats { get; } = [.. AttachmentDocumentFormats.Extracted];

    /// <summary>Gets or sets the greatest number of octets one attachment may hold before it is read at all.</summary>
    /// <remarks>
    /// The bound on what is buffered. Both parsers need to seek, so the content is held in memory for the length of one
    /// extraction, and this is what stops a single attachment from deciding how much memory the process needs. Sixteen
    /// mebibytes is well above the size a mail server accepts an attachment at.
    /// </remarks>
    public long MaxInputOctets { get; set; } = DefaultMaxInputOctets;

    /// <summary>Gets or sets the greatest number of characters one attachment may contribute.</summary>
    /// <remarks>
    /// A ceiling on the output rather than on the input, because the two are not proportional: a compressed page of a
    /// document expands into text at a ratio the sender chooses. The default is the same order as the per-message
    /// ceiling extracted mail text is held to.
    /// </remarks>
    public int MaxExtractedTextCharacters { get; set; } = DefaultMaxExtractedTextCharacters;

    /// <summary>Gets or sets the total octets a container format may decompress to.</summary>
    /// <remarks>
    /// Counted incrementally across every part read, so an archive declaring an enormous uncompressed size is abandoned
    /// while it inflates rather than after it has finished. The declared size is never read: it is the sender's
    /// number, and a bomb is precisely a file that lies about it.
    /// </remarks>
    public long MaxDecompressedOctets { get; set; } = DefaultMaxDecompressedOctets;

    /// <summary>Gets or sets the greatest ratio of decompressed to compressed octets one container part may reach.</summary>
    /// <remarks>
    /// The second half of the same guard, and the half that catches a small archive: a part whose inflation runs far
    /// past what its compressed length can honestly explain is abandoned before <see cref="MaxDecompressedOctets" />
    /// would have been reached. Ordinary Office Open XML markup compresses somewhere below twenty to one.
    /// </remarks>
    public int MaxDecompressionRatio { get; set; } = DefaultMaxDecompressionRatio;

    /// <summary>Gets or sets the greatest number of parts a container format may declare.</summary>
    /// <remarks>An archive of very many tiny parts costs per part rather than per octet, which neither size bound above measures.</remarks>
    public int MaxContainerParts { get; set; } = DefaultMaxContainerParts;

    /// <summary>Gets or sets the greatest depth an element tree inside a container part may nest to.</summary>
    /// <remarks>Deep nesting is what turns a small part into a walk that consumes stack rather than time.</remarks>
    public int MaxElementDepth { get; set; } = DefaultMaxElementDepth;

    /// <summary>Gets or sets the time one extraction may take before it is abandoned.</summary>
    /// <remarks>
    /// Observed between units of work — a PDF page, an archive part, an element — because neither parser accepts a
    /// cancellation token of its own. A parser that never returns from one unit is therefore bounded by the size and
    /// ratio ceilings above rather than by this, which is why those are not optional.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = DefaultTimeout;
}
