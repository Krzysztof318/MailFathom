// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Contacts.Collection;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures one address, or one whole domain, this account never collects a contact from.</summary>
/// <remarks>
/// An entry names a domain or writes a pattern over the address and never both, because the two are different claims
/// and an entry meaning both would be an entry nobody could read. Which one is written decides what the entry is
/// matched against, which <see cref="ContactCollectionExclusion.Excludes" /> states.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ContactCollectionExclusionOptions
{
    /// <summary>Gets or sets the domain this account collects nobody at, or nothing when the entry writes a pattern.</summary>
    public string? Domain { get; set; }

    /// <summary>Gets or sets the pattern over an address this account collects nobody matching, or nothing when the entry names a domain.</summary>
    /// <remarks><c>*</c> stands for any run of characters including none and <c>?</c> for exactly one; everything else is the literal text of an address.</remarks>
    public string? AddressPattern { get; set; }

    /// <summary>Gets or sets whether a domain entry also reaches the names beneath that domain.</summary>
    /// <remarks>
    /// Opt-in per entry rather than a mode the list runs in, for the reason a trusted sender's is: an organization whose
    /// automation lives on names beneath its own domain is excluded in one entry, and one host excluded inside a domain
    /// full of correspondents must not take the rest of the domain with it. It says nothing about a pattern entry, which
    /// can write its own, and startup refuses it there rather than accepting a setting that would do nothing.
    /// </remarks>
    public bool IncludeSubdomains { get; set; }

    /// <summary>Reads the entry as the value collection holds an address against.</summary>
    /// <param name="exclusion">The exclusion, when this configuration names exactly one usable thing to exclude.</param>
    /// <returns><see langword="true" /> when the entry is usable; otherwise <see langword="false" />.</returns>
    internal bool TryCreateExclusion([NotNullWhen(true)] out ContactCollectionExclusion? exclusion)
    {
        exclusion = null;

        var namesDomain = !string.IsNullOrWhiteSpace(this.Domain);
        var namesPattern = !string.IsNullOrWhiteSpace(this.AddressPattern);

        if (namesDomain == namesPattern)
        {
            return false;
        }

        return namesDomain
            ? ContactCollectionExclusion.TryCreateForDomain(this.Domain, this.IncludeSubdomains, out exclusion)
            : !this.IncludeSubdomains
                && ContactCollectionExclusion.TryCreateForAddressPattern(this.AddressPattern, out exclusion);
    }
}
