// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures one sender an account recognizes without anything else vouching for them.</summary>
/// <remarks>
/// <para>
/// An entry names a domain or an address and never both, because the two are different claims and an entry meaning
/// both would be an entry nobody could read. Which one is written decides what the entry is matched against, which
/// <see cref="TrustedSenderEntry.Matches" /> states.
/// </para>
/// <para>
/// This is the declared half of the list. The half somebody adds while the deployment is running lives in the database,
/// is not editable by a configuration reload, and cannot shadow anything written here.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class TrustedSenderOptions
{
    /// <summary>Gets or sets the domain this account recognizes, or nothing when the entry names an address.</summary>
    public string? Domain { get; set; }

    /// <summary>Gets or sets the single address this account recognizes, or nothing when the entry names a domain.</summary>
    public string? Address { get; set; }

    /// <summary>Gets or sets whether a domain entry also reaches the names beneath that domain.</summary>
    /// <remarks>
    /// Opt-in per entry rather than a mode the list runs in, because both answers are needed: an organization signing
    /// everything as one name wants its subdomains, and one host recognized inside a domain full of unrecognized ones
    /// must not drag the rest in. It says nothing about an address entry and startup refuses it there rather than
    /// accepting a setting that would do nothing.
    /// </remarks>
    public bool IncludeSubdomains { get; set; }

    /// <summary>Reads the entry as the domain value the matcher holds a sender against.</summary>
    /// <param name="entry">The entry, when this configuration names exactly one usable sender.</param>
    /// <returns><see langword="true" /> when the entry is usable; otherwise <see langword="false" />.</returns>
    internal bool TryCreateEntry([NotNullWhen(true)] out TrustedSenderEntry? entry)
    {
        entry = null;

        var namesDomain = !string.IsNullOrWhiteSpace(this.Domain);
        var namesAddress = !string.IsNullOrWhiteSpace(this.Address);

        if (namesDomain == namesAddress)
        {
            return false;
        }

        return namesDomain
            ? TrustedSenderEntry.TryCreateForDomain(this.Domain, this.IncludeSubdomains, out entry)
            : !this.IncludeSubdomains && TrustedSenderEntry.TryCreateForAddress(this.Address, out entry);
    }
}
