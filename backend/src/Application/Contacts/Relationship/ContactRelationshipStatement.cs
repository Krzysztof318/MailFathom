// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts.Relationship;

/// <summary>One thing a derivation says about a correspondence, with what it rests on.</summary>
/// <remarks>
/// <para>
/// Every statement carries at least one citation, and one that carries none never becomes a statement at all. A
/// relationship card is read <em>instead of</em> the correspondence it was derived from — that is what makes it worth
/// drawing — so a sentence with nothing behind it would sit beside the sourced ones looking exactly like them. The
/// same reading a conversation's own derived state is written under, and the opposite of a drafted reply's, where an
/// unsupported claim is the thing its author has to see before they send it.
/// </para>
/// <para>
/// The text is somebody's mail read back to them and is bounded here rather than trusted from a producer: a card holds
/// a sentence or two per statement, and anything past that is a model retelling the correspondence rather than saying
/// what it amounts to.
/// </para>
/// </remarks>
public sealed record ContactRelationshipStatement
{
    /// <summary>The greatest length a statement carries before it is shortened to it.</summary>
    /// <remarks>Two or three sentences, which is the note at the head of the card; every other statement is shorter by what it is rather than by a bound of its own.</remarks>
    public const int MaximumTextLength = 400;

    /// <summary>The greatest number of conversations or documents one statement cites.</summary>
    /// <remarks>The bound every other citation in this system is written under: past a handful a producer is citing the correspondence rather than the place its statement came from.</remarks>
    public const int MaximumSourceCount = 4;

    private ContactRelationshipStatement(string text, IReadOnlyList<ContactRelationshipSource> sources)
    {
        this.Text = text;
        this.Sources = sources;
    }

    /// <summary>Gets what the derivation said.</summary>
    public string Text { get; }

    /// <summary>Gets the conversations and documents it rests on, in the order the producer named them.</summary>
    public IReadOnlyList<ContactRelationshipSource> Sources { get; }

    /// <summary>Records one statement, or nothing where the correspondence does not back it.</summary>
    /// <param name="text">What the derivation said, which may be blank where a producer wrote nothing under the heading.</param>
    /// <param name="sources">The conversations and documents backing it.</param>
    /// <returns>The statement, or <see langword="null" /> where there is no text or nothing backing it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It answers with nothing rather than refusing, because a producer leaving a heading out and a producer writing
    /// one nothing supports are both ordinary answers about a correspondence — a person with one exchange and no
    /// documents genuinely has no case to name. Only a card with no note at all is worth reporting, and the derivation
    /// above reports it.
    /// </remarks>
    public static ContactRelationshipStatement? Create(string? text, IReadOnlyList<ContactRelationshipSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cited = sources.Distinct().Take(MaximumSourceCount).ToArray();

        if (cited.Length is 0)
        {
            return null;
        }

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return new ContactRelationshipStatement(
            MailTextBounds.TruncateAtTextElementBoundary(collapsed, MaximumTextLength),
            cited);
    }
}
