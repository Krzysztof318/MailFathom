// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>The people and organizations a question is about, and where each of them stands in the correspondence.</summary>
/// <remarks>
/// The block for "who at their end handles this", "who have I been dealing with". What it presents is what the mail
/// says: a relationship is read out of the correspondence rather than known, so each entry names the messages it was
/// read from and a client can show a reader why the assistant thinks somebody is who it says they are.
/// </remarks>
public sealed record PeopleBlock : PresentationBlock
{
    /// <summary>The greatest number of entries one block may hold.</summary>
    public const int MaxEntries = 30;

    /// <summary>Initializes the people a question is about.</summary>
    /// <param name="evidence">What the correspondence does for the block as a whole.</param>
    /// <param name="entries">The entries, most relevant first.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> or <paramref name="entries" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when there are no entries or more than <see cref="MaxEntries" /> of them.</exception>
    public PeopleBlock(PresentationEvidence evidence, IReadOnlyList<PersonEntry> entries)
        : base(PresentationBlockType.People, evidence) =>
        this.Entries = PresentationRequirement.RequiredItems(entries, MaxEntries, nameof(entries));

    /// <summary>Gets the entries, most relevant first.</summary>
    public IReadOnlyList<PersonEntry> Entries { get; }

    /// <inheritdoc />
    public override IEnumerable<PresentationCitationId> ReferencedCitations =>
        base.ReferencedCitations.Concat(this.Entries.SelectMany(entry => entry.Sources));
}

/// <summary>One person or organization, as the correspondence identifies them.</summary>
/// <remarks>
/// The address is optional because an organization is often named without one, and a person can be identified in the
/// body of a message that they never sent. Where there is one it is the identity a client acts on; the display name is
/// what a reader recognizes and is never used as one.
/// </remarks>
public sealed record PersonEntry
{
    /// <summary>Initializes one person or organization.</summary>
    /// <param name="displayName">The name a reader recognizes them by.</param>
    /// <param name="address">Their address, or <see langword="null" /> where the correspondence identifies them without one.</param>
    /// <param name="relationship">Where they stand in the correspondence, as it reads.</param>
    /// <param name="lastContactAt">When they were last in contact, or <see langword="null" /> where no message establishes it.</param>
    /// <param name="sources">The citations this entry rests on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a text is the unspecified default, or a citation is unspecified or named twice.</exception>
    public PersonEntry(
        PresentationText displayName,
        EmailAddress? address,
        PresentationText relationship,
        DateTimeOffset? lastContactAt,
        IReadOnlyList<PresentationCitationId> sources)
    {
        PresentationRequirement.Specified(displayName, nameof(displayName));
        PresentationRequirement.Specified(relationship, nameof(relationship));

        this.DisplayName = displayName;
        this.Address = address;
        this.Relationship = relationship;
        this.LastContactAt = lastContactAt;
        this.Sources = PresentationRequirement.Sources(sources, nameof(sources));
    }

    /// <summary>Gets the name a reader recognizes them by.</summary>
    public PresentationText DisplayName { get; }

    /// <summary>Gets their address, or <see langword="null" /> where the correspondence identifies them without one.</summary>
    public EmailAddress? Address { get; }

    /// <summary>Gets where they stand in the correspondence, as it reads.</summary>
    public PresentationText Relationship { get; }

    /// <summary>Gets when they were last in contact, or <see langword="null" /> where no message establishes it.</summary>
    public DateTimeOffset? LastContactAt { get; }

    /// <summary>Gets the citations this entry rests on.</summary>
    public IReadOnlyList<PresentationCitationId> Sources { get; }
}
