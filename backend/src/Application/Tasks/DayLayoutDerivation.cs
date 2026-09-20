// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Tasks;

/// <summary>What laying out one day produced: the arrangement it settled, or the reason it produced none.</summary>
/// <remarks>
/// The two outcomes are what a screen draws differently. A settled arrangement is offered to the person, including a
/// settled arrangement of nothing — a day whose tasks all fit around what is already on it is laid out by saying so.
/// A withheld one is a sentence about this deployment rather than about the day, and the control stays where it was so
/// the person may press it again.
/// </remarks>
public sealed record DayLayoutDerivation
{
    private DayLayoutDerivation(DayLayoutSuggestion? suggestion, DayLayoutWithholding? withheld)
    {
        this.Suggestion = suggestion;
        this.Withheld = withheld;
    }

    /// <summary>Gets the arrangement that was offered, or <see langword="null" /> where none was.</summary>
    public DayLayoutSuggestion? Suggestion { get; }

    /// <summary>Gets why no arrangement was offered, or <see langword="null" /> where one was.</summary>
    public DayLayoutWithholding? Withheld { get; }

    /// <summary>Records an arrangement to offer.</summary>
    /// <param name="suggestion">The arrangement.</param>
    /// <returns>The derivation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="suggestion" /> is <see langword="null" />.</exception>
    public static DayLayoutDerivation Settled(DayLayoutSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        return new DayLayoutDerivation(suggestion, withheld: null);
    }

    /// <summary>Records that no arrangement was produced, and why.</summary>
    /// <param name="withholding">The condition that stopped it.</param>
    /// <returns>The derivation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="withholding" /> is not a defined member.</exception>
    public static DayLayoutDerivation Withholding(DayLayoutWithholding withholding)
    {
        if (!Enum.IsDefined(withholding))
        {
            throw new ArgumentOutOfRangeException(
                nameof(withholding),
                withholding,
                "A withheld arrangement names one of the conditions this system stops on.");
        }

        return new DayLayoutDerivation(suggestion: null, withholding);
    }
}
