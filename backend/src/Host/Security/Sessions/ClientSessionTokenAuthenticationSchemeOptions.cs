// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Authentication;

namespace MailFathom.Host.Security.Sessions;

/// <summary>Which surface one session-token scheme protects.</summary>
/// <remarks>
/// There is no attempt bound here, unlike the password scheme's options, and its absence is the point of the whole
/// exchange: a bound exists to make guessing a password expensive, and there is nothing to guess here — a token is 256
/// bits this process drew from the platform's cryptographic generator, and a caller working through that space meets
/// the surface's own rate limiter long before it meets anything else.
/// </remarks>
internal sealed class ClientSessionTokenAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    /// <summary>Gets or sets the surface this scheme protects, which names the scheme a success reports itself under.</summary>
    internal TransportSurface Surface { get; set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the scheme was registered without a surface, which would leave an admitted request carrying no identity.</exception>
    public override void Validate()
    {
        base.Validate();

        if (!this.Surface.IsSpecified)
        {
            throw new InvalidOperationException(
                "The session-token authentication scheme was registered without a transport surface.");
        }
    }
}
