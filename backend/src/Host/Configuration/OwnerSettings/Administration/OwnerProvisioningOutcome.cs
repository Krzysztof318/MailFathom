// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>What provisioning an owner produced: the identifier they were minted under, or why none was.</summary>
/// <remarks>
/// A refusal is a result rather than an exception because every one of them is a decision the administrator asking for
/// the owner acts on: a label somebody already carries, a roster at the bound, and a deployment whose endpoints could
/// not tell one owner's caller from another's. None of them is a failure of the machinery underneath.
/// </remarks>
internal sealed record OwnerProvisioningOutcome
{
    private OwnerProvisioningOutcome(MailOwnerId owner, string? refusalMessage)
    {
        this.Owner = owner;
        this.RefusalMessage = refusalMessage;
    }

    /// <summary>Gets the identifier the owner was minted under, which is meaningful only when one was.</summary>
    public MailOwnerId Owner { get; }

    /// <summary>Gets the sentence naming why no owner was recorded, or <see langword="null" /> when one was.</summary>
    public string? RefusalMessage { get; }

    /// <summary>Gets whether this deployment now holds the owner.</summary>
    public bool IsProvisioned => this.RefusalMessage is null;

    /// <summary>Reports an owner this deployment now holds.</summary>
    /// <param name="owner">The identifier they were minted under.</param>
    /// <returns>The provisioned result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    public static OwnerProvisioningOutcome Provisioned(MailOwnerId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("A provisioned owner is named by the identifier they were minted under.", nameof(owner));
        }

        return new OwnerProvisioningOutcome(owner, refusalMessage: null);
    }

    /// <summary>Reports that no owner was recorded, and what has to change first.</summary>
    /// <param name="refusalMessage">The sentence naming the correction.</param>
    /// <returns>The refused result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusalMessage" /> is <see langword="null" />, empty, or white space.</exception>
    public static OwnerProvisioningOutcome Refused(string refusalMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refusalMessage);

        return new OwnerProvisioningOutcome(owner: default, refusalMessage);
    }
}
