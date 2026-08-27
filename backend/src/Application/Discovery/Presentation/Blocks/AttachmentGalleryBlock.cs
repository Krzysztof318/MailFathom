// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>The files a question is about, each with the message it arrived on.</summary>
/// <remarks>
/// The block for "where is the signed copy", "what did they send me last quarter". Each entry cites the message the
/// file arrived on rather than pointing at the file directly: an attachment is only ever reached through the mail that
/// carried it, and a reader deciding whether this is the right file usually needs to see which message it was.
/// </remarks>
public sealed record AttachmentGalleryBlock : PresentationBlock
{
    /// <summary>The greatest number of entries one gallery may hold.</summary>
    public const int MaxEntries = 50;

    /// <summary>Initializes the files a question is about.</summary>
    /// <param name="evidence">What the correspondence does for the gallery as a whole.</param>
    /// <param name="entries">The entries, most relevant first.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> or <paramref name="entries" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when there are no entries or more than <see cref="MaxEntries" /> of them.</exception>
    public AttachmentGalleryBlock(PresentationEvidence evidence, IReadOnlyList<AttachmentEntry> entries)
        : base(PresentationBlockType.AttachmentGallery, evidence) =>
        this.Entries = PresentationRequirement.RequiredItems(entries, MaxEntries, nameof(entries));

    /// <summary>Gets the entries, most relevant first.</summary>
    public IReadOnlyList<AttachmentEntry> Entries { get; }

    /// <inheritdoc />
    public override IEnumerable<PresentationCitationId> ReferencedCitations =>
        base.ReferencedCitations.Concat(this.Entries.Select(entry => entry.Source));
}

/// <summary>One file found in mail.</summary>
/// <remarks>
/// The media type is what the message declared rather than what the content is, so a client uses it to choose an icon
/// and never to decide how to open something. The size is the message's own account of it, which is why an entry whose
/// content was never stored still has one.
/// </remarks>
public sealed record AttachmentEntry
{
    /// <summary>Initializes one file found in mail.</summary>
    /// <param name="source">The citation resolving to the attachment.</param>
    /// <param name="name">The file's name, normalized.</param>
    /// <param name="mediaType">The media type the message declared, or <see langword="null" /> where it declared none.</param>
    /// <param name="sizeOctets">How large the message says the file is.</param>
    /// <param name="availability">Whether the file can be opened.</param>
    /// <exception cref="ArgumentException">Thrown when the source or the name is the unspecified default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="sizeOctets" /> is negative.</exception>
    public AttachmentEntry(
        PresentationCitationId source,
        PresentationText name,
        PresentationText? mediaType,
        long sizeOctets,
        AttachmentAvailability availability)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeOctets);
        PresentationRequirement.Specified(name, nameof(name));

        if (mediaType is { } declared)
        {
            PresentationRequirement.Specified(declared, nameof(mediaType));
        }

        if (!source.IsSpecified)
        {
            throw new ArgumentException("An attachment entry names the citation it presents.", nameof(source));
        }

        this.Source = source;
        this.Name = name;
        this.MediaType = mediaType;
        this.SizeOctets = sizeOctets;
        this.Availability = availability;
    }

    /// <summary>Gets the citation resolving to the attachment.</summary>
    public PresentationCitationId Source { get; }

    /// <summary>Gets the file's name, normalized.</summary>
    public PresentationText Name { get; }

    /// <summary>Gets the media type the message declared, or <see langword="null" /> where it declared none.</summary>
    public PresentationText? MediaType { get; }

    /// <summary>Gets how large the message says the file is.</summary>
    public long SizeOctets { get; }

    /// <summary>Gets whether the file can be opened.</summary>
    public AttachmentAvailability Availability { get; }
}
