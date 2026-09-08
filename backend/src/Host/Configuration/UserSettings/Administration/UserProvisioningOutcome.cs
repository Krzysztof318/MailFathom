// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What provisioning a user produced: the identifier they were minted under, or why none was.</summary>
/// <remarks>
/// A refusal is a result rather than an exception because every one of them is a decision the administrator asking for
/// the user acts on: a label somebody already carries, a roster at the bound, and a deployment whose endpoints could
/// not tell one user's caller from another's. None of them is a failure of the machinery underneath.
/// </remarks>
internal sealed record UserProvisioningOutcome
{
    private UserProvisioningOutcome(MailUserId user, string? refusalMessage)
    {
        this.User = user;
        this.RefusalMessage = refusalMessage;
    }

    /// <summary>Gets the identifier the user was minted under, which is meaningful only when one was.</summary>
    public MailUserId User { get; }

    /// <summary>Gets the sentence naming why no user was recorded, or <see langword="null" /> when one was.</summary>
    public string? RefusalMessage { get; }

    /// <summary>Gets whether this deployment now holds the user.</summary>
    public bool IsProvisioned => this.RefusalMessage is null;

    /// <summary>Reports a user this deployment now holds.</summary>
    /// <param name="user">The identifier they were minted under.</param>
    /// <returns>The provisioned result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    public static UserProvisioningOutcome Provisioned(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A provisioned user is named by the identifier they were minted under.", nameof(user));
        }

        return new UserProvisioningOutcome(user, refusalMessage: null);
    }

    /// <summary>Reports that no user was recorded, and what has to change first.</summary>
    /// <param name="refusalMessage">The sentence naming the correction.</param>
    /// <returns>The refused result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusalMessage" /> is <see langword="null" />, empty, or white space.</exception>
    public static UserProvisioningOutcome Refused(string refusalMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refusalMessage);

        return new UserProvisioningOutcome(user: default, refusalMessage);
    }
}
