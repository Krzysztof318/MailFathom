// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Contacts.Relationship;

/// <summary>Where a correspondence with one person stands, as an opened contact reports it.</summary>
/// <remarks>
/// <para>
/// <b>It is derived on the read and stored nowhere.</b> Nothing here is written to a contact, to a message, or to a
/// table of its own, so a card can never come to disagree with the mail it was read from: the next person to open the
/// contact derives it again from whatever the correspondence says then.
/// </para>
/// <para>
/// <see cref="Nothing" /> is what every case that produced no card answers with, and it is not a failure a client
/// reports. A deployment that derives none, a provider that could not be reached, an allowance already spent, a
/// producer that answered unreadably, and a person this mailbox holds no mail about all leave the contact drawn from
/// its record and its correspondence alone — which is the contact page every deployment served before this existed.
/// </para>
/// <para>
/// The note is what makes the rest worth drawing, so an answer without one is <see cref="Nothing" /> however many
/// observations survived beside it. A card headed by nothing, carrying three chips a reader never asked for, is a
/// worse answer than no card.
/// </para>
/// </remarks>
public sealed record ContactRelationship
{
    private ContactRelationship(
        ContactRelationshipStatement? note,
        ContactRelationshipStatement? nextAction,
        IReadOnlyList<ContactRelationshipObservation> observations)
    {
        this.Note = note;
        this.NextAction = nextAction;
        this.Observations = observations;
    }

    /// <summary>Gets the answer every derivation that produced no card gives.</summary>
    public static ContactRelationship Nothing { get; } = new(note: null, nextAction: null, []);

    /// <summary>Gets whether a card was derived at all.</summary>
    public bool WasDerived => this.Note is not null;

    /// <summary>Gets the note the card is headed by, or <see langword="null" /> where none was derived.</summary>
    public ContactRelationshipStatement? Note { get; }

    /// <summary>Gets what the derivation suggests doing next, or <see langword="null" /> where it suggested nothing.</summary>
    /// <remarks>Optional because a correspondence that is settled has no next action, and a suggestion invented for one would be the first thing a reader stopped believing.</remarks>
    public ContactRelationshipStatement? NextAction { get; }

    /// <summary>Gets what the derivation observed beside the note, in aspect order, which is empty where it observed nothing.</summary>
    public IReadOnlyList<ContactRelationshipObservation> Observations { get; }

    /// <summary>Records one derived card.</summary>
    /// <param name="note">The note the card is headed by, or <see langword="null" /> where none survived.</param>
    /// <param name="nextAction">What to do next, or <see langword="null" /> where the derivation suggested nothing.</param>
    /// <param name="observations">What it observed beside the note, at most one per aspect.</param>
    /// <returns>The card, or <see cref="Nothing" /> where no note survived.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="observations" /> is <see langword="null" />.</exception>
    public static ContactRelationship Derived(
        ContactRelationshipStatement? note,
        ContactRelationshipStatement? nextAction,
        IReadOnlyList<ContactRelationshipObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        return note is null
            ? Nothing
            : new ContactRelationship(
                note,
                nextAction,
                [.. observations.DistinctBy(static observation => observation.Aspect).OrderBy(static observation => observation.Aspect)]);
    }
}
