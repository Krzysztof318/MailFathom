// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security.Transport;

/// <summary>Authenticates nobody, which is what the application's default scheme does for every request no surface pre-authenticates.</summary>
/// <remarks>
/// <para>
/// It judges no credential and holds no configuration, because deciding what to authenticate is
/// <see cref="DefaultTransportAuthentication.PreAuthenticatingSchemeFor" />'s and judging a credential is a surface's.
/// Where that decision names a surface, the framework's own forwarding hands the request to that surface's routing
/// scheme and this handler is never reached; where it names none, this is the answer, and no result is what leaves the
/// request anonymous rather than failed — a request that presented nothing has nothing to refuse, and one whose
/// credential belongs to a surface authenticating later must reach that surface unjudged.
/// </para>
/// <para>
/// The inherited challenge and forbid are equally deliberate. Both surfaces' requirements name their own routing
/// scheme, so a refusal is worded by the surface that refused it and never by this; what remains here answers the
/// caller that provoked a challenge through no endpoint of either surface, and a bare status is all such a request has
/// earned.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The authentication framework materializes this handler for its registered scheme.")]
internal sealed class DefaultTransportAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Initializes a new default transport authentication handler.</summary>
    public DefaultTransportAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder urlEncoder)
        : base(schemeOptions, loggerFactory, urlEncoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());
}
