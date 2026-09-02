// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>The guards every block in the catalogue applies to the values it is built from.</summary>
/// <remarks>
/// <para>
/// Two of them, stated once because nine blocks apply them. A text value must be specified rather than the reachable
/// struct default, and every list a plan carries is bounded and non-empty where the block would mean nothing empty — a
/// timeline with no entries is a block a client draws as a heading over nothing, and an unbounded list is a screen a
/// model can make arbitrarily long.
/// </para>
/// <para>
/// The bounds are the block's own and are passed in, because what "too many" means differs: a table of twenty rows is
/// ordinary and twenty draft recipients is a mistake.
/// </para>
/// </remarks>
internal static class PresentationRequirement
{
    /// <summary>Refuses a text that is the unusable struct default.</summary>
    /// <param name="text">The text to check.</param>
    /// <param name="parameterName">The parameter the text arrived as.</param>
    /// <exception cref="ArgumentException">Thrown when the text is the unspecified default.</exception>
    internal static void Specified(PresentationText text, string parameterName)
    {
        if (!text.IsSpecified)
        {
            throw new ArgumentException("A presentation text cannot be the unspecified default.", parameterName);
        }
    }

    /// <summary>Copies a list a block cannot be drawn without, refusing an empty or oversized one.</summary>
    /// <typeparam name="TItem">What the list holds.</typeparam>
    /// <param name="items">The list to copy.</param>
    /// <param name="maximum">The greatest number of items this block may hold.</param>
    /// <param name="parameterName">The parameter the list arrived as.</param>
    /// <returns>A copy of the list, which the block owns.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="items" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the list is empty or holds more than <paramref name="maximum" /> items.</exception>
    internal static IReadOnlyList<TItem> RequiredItems<TItem>(
        IReadOnlyList<TItem> items,
        int maximum,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(items, parameterName);

        if (items.Count == 0)
        {
            throw new ArgumentException("The block presents nothing when this list is empty.", parameterName);
        }

        return OptionalItems(items, maximum, parameterName);
    }

    /// <summary>Copies a list a block may legitimately hold none of, refusing an oversized one.</summary>
    /// <typeparam name="TItem">What the list holds.</typeparam>
    /// <param name="items">The list to copy.</param>
    /// <param name="maximum">The greatest number of items this block may hold.</param>
    /// <param name="parameterName">The parameter the list arrived as.</param>
    /// <returns>A copy of the list, which the block owns.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="items" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the list holds more than <paramref name="maximum" /> items.</exception>
    internal static IReadOnlyList<TItem> OptionalItems<TItem>(
        IReadOnlyList<TItem> items,
        int maximum,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(items, parameterName);

        if (items.Count > maximum)
        {
            throw new ArgumentException($"This list holds at most {maximum} items.", parameterName);
        }

        if (items.Any(item => item is null))
        {
            throw new ArgumentException("A list a plan carries holds no null item.", parameterName);
        }

        return [.. items];
    }

    /// <summary>Copies the citations one item within a block rests on, refusing an unspecified or repeated one.</summary>
    /// <param name="sources">The citations the item rests on.</param>
    /// <param name="parameterName">The parameter the citations arrived as.</param>
    /// <returns>A copy of the citations, which the item owns.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a citation is unspecified, named twice, or there are more than <see cref="PresentationEvidence.MaxCitations" /> of them.</exception>
    /// <remarks>
    /// An item may rest on nothing, which is how a row nothing backs is presented beside rows that are backed; what the
    /// block as a whole rests on is <see cref="PresentationBlock.Evidence" />, which is held to the stricter rule.
    /// </remarks>
    internal static IReadOnlyList<PresentationCitationId> Sources(
        IReadOnlyList<PresentationCitationId> sources,
        string parameterName)
    {
        var copied = OptionalItems(sources, PresentationEvidence.MaxCitations, parameterName);

        if (copied.Any(source => !source.IsSpecified))
        {
            throw new ArgumentException("A citation reference cannot be the unspecified default.", parameterName);
        }

        if (copied.Distinct().Count() != copied.Count)
        {
            throw new ArgumentException("Each citation is named once.", parameterName);
        }

        return copied;
    }
}
