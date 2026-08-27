// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Authentication;

namespace MailFathom.Host.Security.Basic;

/// <summary>What one surface's Basic scheme grants an admitted owner, and how often it lets one be tried.</summary>
/// <remarks>
/// There is no credential list here, unlike every other scheme's options, and its absence is the method: the passwords
/// are records in the deployment's own database rather than material an operator wrote into a section, so what the
/// scheme carries is the entry's grant and the entry's bound. Both reach the handler through the framework's own
/// per-scheme options for the reason the keys do — it is what lets two surfaces register the same handler over two
/// different grants without the handler acquiring a settings object of its own.
/// </remarks>
internal sealed class BasicAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    /// <summary>Gets or sets the surface this scheme protects, which names the scheme a success reports itself under.</summary>
    internal TransportSurface Surface { get; set; }

    /// <summary>Gets or sets what the entry carrying the Basic block granted, which every owner admitted here holds.</summary>
    /// <remarks>
    /// One grant for the method rather than one per credential, because a password names a credential row and no row
    /// names a configuration entry. Narrowing what one owner may do relative to another is not something this method
    /// expresses; a deployment that needs it configures a second surface or a credential of a different kind.
    /// </remarks>
    internal IReadOnlyList<MailFathomPermission> Grant { get; set; } = [];

    /// <summary>Gets or sets how many attempts one source and one username each get per minute.</summary>
    internal int AttemptsPerMinute { get; set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the scheme was registered without a surface or without a bound, either of which would leave an admitted request carrying no identity or an unbounded number of guesses.</exception>
    public override void Validate()
    {
        base.Validate();

        if (!this.Surface.IsSpecified)
        {
            throw new InvalidOperationException(
                "The Basic authentication scheme was registered without a transport surface.");
        }

        if (this.AttemptsPerMinute <= 0)
        {
            throw new InvalidOperationException(
                "The Basic authentication scheme was registered without a positive attempt bound, which would refuse "
                + "every request rather than bounding any.");
        }
    }
}
