// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.Mail.OAuth;

/// <summary>One account's OAuth 2.0 access token together with the instant it stops being usable.</summary>
/// <param name="Value">The bearer token a SASL mechanism presents to the mail server.</param>
/// <param name="ExpiresAt">The instant the authorization server said the token stops being accepted.</param>
/// <remarks>
/// <para>
/// The token is a credential and this type is deliberately not a <see cref="ResolvedSecret" />. That type owns
/// a pinned buffer it erases on disposal, which is the right shape for material read from a file or an environment
/// variable and used once. An access token instead arrives as text inside a JSON response, is cached across
/// connection attempts by design, and is handed to MailKit as a <see cref="string" />, so the .NET string it was parsed
/// from already exists on the managed heap before any owned buffer could be filled. Wrapping it would erase a copy
/// while leaving the original for the garbage collector, which buys the appearance of protection rather than the fact
/// of it.
/// </para>
/// <para>
/// What does hold: the value never reaches a log, an error message, an audit event, or an MCP response, and
/// <see cref="ToString" /> is redacted so a record carrying this value cannot print it through synthesized printing.
/// </para>
/// </remarks>
internal sealed record MailAccessToken(string Value, DateTimeOffset ExpiresAt)
{
    /// <summary>Determines whether the token should be replaced rather than presented.</summary>
    /// <param name="asOf">The current instant, read from an injected <see cref="TimeProvider" />.</param>
    /// <param name="refreshSkew">How far before expiry the token stops being offered.</param>
    /// <returns><see langword="true" /> when the token is expired or inside its refresh skew.</returns>
    /// <remarks>
    /// The skew is what makes the refresh proactive. A token handed out with one second remaining would be accepted
    /// here and rejected by the server moments later, after the connection attempt it was fetched for had already
    /// started, which turns a predictable refresh into an authentication failure that looks like a transport fault.
    /// </remarks>
    public bool IsDueForRefresh(DateTimeOffset asOf, TimeSpan refreshSkew) => this.ExpiresAt - refreshSkew <= asOf;

    /// <inheritdoc />
    /// <remarks>Redacted by construction, so neither this record nor one embedding it can print the token.</remarks>
    public override string ToString() => "***";
}
