// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>What relabelling one owner did: nothing at all, nothing this deployment holds, or the label.</summary>
/// <param name="OwnerHeld">Whether this deployment holds an owner under that identifier.</param>
/// <param name="RefusalMessage">The sentence naming what has to change first, or <see langword="null" /> where the row carries the label.</param>
/// <remarks>
/// The two facts are apart because a boundary answers them apart. An owner this deployment does not hold is the same
/// answer here as at every other owner-scoped route — the deployment holds no such record — while a label somebody else
/// carries is a refusal about the label rather than about the owner, and reporting the first as the second would give a
/// credential granted the write and not the read a way to ask which owners exist.
/// </remarks>
internal readonly record struct OwnerRelabelOutcome(bool OwnerHeld, string? RefusalMessage)
{
    /// <summary>Gets the outcome of a row that now carries the label.</summary>
    internal static OwnerRelabelOutcome Relabelled { get; } = new(OwnerHeld: true, RefusalMessage: null);

    /// <summary>Gets the outcome of an identifier this deployment holds no owner under.</summary>
    internal static OwnerRelabelOutcome NoSuchOwner { get; } = new(OwnerHeld: false, RefusalMessage: null);

    /// <summary>Gets whether the row carries the label.</summary>
    internal bool IsRelabelled => this.OwnerHeld && this.RefusalMessage is null;

    /// <summary>Reports that the owner is held and the label was not written.</summary>
    /// <param name="refusalMessage">The sentence naming the correction.</param>
    /// <returns>The refused outcome.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusalMessage" /> is <see langword="null" />, empty, or white space.</exception>
    internal static OwnerRelabelOutcome Refused(string refusalMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refusalMessage);

        return new OwnerRelabelOutcome(OwnerHeld: true, refusalMessage);
    }
}
