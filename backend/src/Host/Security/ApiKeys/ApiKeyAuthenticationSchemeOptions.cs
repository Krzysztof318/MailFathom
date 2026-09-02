// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.AspNetCore.Authentication;

namespace MailFathom.Host.Security.ApiKeys;

/// <summary>Which keys one surface's API key scheme compares a presented credential against.</summary>
/// <remarks>
/// The keys reach the handler through the framework's own per-scheme options rather than through the endpoint settings,
/// which is what lets two surfaces register the same handler over two different key lists. Reading a settings object
/// instead would tie the handler to one section, and a second surface would have had to bring a second handler.
/// <para>
/// The references are held rather than the material behind them. What a reference resolves to is read per request by
/// <see cref="Infrastructure.Security.ApiKeys.ApiKeyAuthenticator" />, so a key rotated in place reaches the next request
/// without a restart.
/// </para>
/// </remarks>
internal sealed class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    /// <summary>Gets or sets the surface this scheme protects, which names the claim identity a success produces.</summary>
    internal TransportSurface Surface { get; set; }

    /// <summary>Gets or sets the API key references a request may present one of, empty when the surface accepts none.</summary>
    internal IReadOnlyList<ConfiguredSecret> ApiKeys { get; set; } = [];

    /// <summary>Gets or sets what each key's own configuration entry granted, keyed by the key's configured name.</summary>
    /// <remarks>
    /// Resolved once while the host is composed rather than read per request, and carried here for the reason the keys
    /// themselves are: it is the scheme's own option, so two surfaces register one handler over two grants. A key
    /// missing from the map holds nothing, which is the same answer an entry that granted nothing gives — and it cannot
    /// arise while the map and the key list are composed from the same entries.
    /// </remarks>
    internal IReadOnlyDictionary<string, IReadOnlyList<MailFathomPermission>> GrantsByKeyName { get; set; } =
        new Dictionary<string, IReadOnlyList<MailFathomPermission>>(StringComparer.Ordinal);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the scheme was registered without a surface, which would leave a successful authentication carrying no identity.</exception>
    public override void Validate()
    {
        base.Validate();

        if (!this.Surface.IsSpecified)
        {
            throw new InvalidOperationException(
                "The API key authentication scheme was registered without a transport surface.");
        }
    }
}
