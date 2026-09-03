// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>What changing the name a person is recorded under did: nothing this deployment holds, a refusal, or the name.</summary>
/// <param name="OwnerHeld">Whether this deployment holds a record for the person the change was made for.</param>
/// <param name="Recorded">The name the row now carries, as it was stored, or <see langword="null" /> where nothing was written.</param>
/// <param name="RefusalMessage">The sentence naming what to correct, or <see langword="null" /> where the name was recorded.</param>
/// <remarks>
/// It carries the recorded name rather than leaving a caller to recompose it from what was sent, because a name is
/// trimmed on its way in: a client redrawing what somebody typed would show a name this deployment does not hold, and
/// deriving it at the boundary would put the trimming rule in two places for a reader to notice they agree.
/// <para>
/// The absence of a record is apart from a refusal for the reason <see cref="OwnerRelabelOutcome" /> keeps them apart:
/// a person this deployment does not hold is the same answer as at every other owner-scoped route, while a name
/// somebody else carries is about the name rather than about the person.
/// </para>
/// </remarks>
internal readonly record struct OwnDisplayNameChange(bool OwnerHeld, string? Recorded, string? RefusalMessage)
{
    /// <summary>Gets the outcome of a person this deployment holds no record for.</summary>
    internal static OwnDisplayNameChange NoSuchOwner { get; } = new(OwnerHeld: false, Recorded: null, RefusalMessage: null);

    /// <summary>Reports that the row now carries the name.</summary>
    /// <param name="recorded">The name as it was stored.</param>
    /// <returns>The recorded outcome.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="recorded" /> is <see langword="null" />, empty, or white space.</exception>
    internal static OwnDisplayNameChange Recording(string recorded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recorded);

        return new OwnDisplayNameChange(OwnerHeld: true, recorded, RefusalMessage: null);
    }

    /// <summary>Reports that the person is held and the name was not written.</summary>
    /// <param name="refusalMessage">The sentence naming the correction.</param>
    /// <returns>The refused outcome.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusalMessage" /> is <see langword="null" />, empty, or white space.</exception>
    internal static OwnDisplayNameChange Refused(string refusalMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refusalMessage);

        return new OwnDisplayNameChange(OwnerHeld: true, Recorded: null, refusalMessage);
    }
}
