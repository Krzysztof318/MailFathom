// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Authentication;

namespace MailFathom.Host.Security.Basic;

/// <summary>Which surface one Basic scheme protects, and how often it lets a password be tried.</summary>
/// <remarks>
/// There is no credential list here and no grant, unlike the configured schemes' options, and their absence is the
/// method: the passwords are records in the deployment's own database rather than material an operator wrote into a
/// section, and what each one grants is recorded beside the user it resolves. What is left for the scheme to carry is
/// the surface and the bound, and both reach the handler through the framework's own per-scheme options — which is
/// what lets two surfaces register the same handler over two different bounds without the handler acquiring a settings
/// object of its own.
/// </remarks>
internal sealed class BasicAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    /// <summary>Gets or sets the surface this scheme protects, which names the scheme a success reports itself under.</summary>
    internal TransportSurface Surface { get; set; }

    /// <summary>Gets or sets how many attempts one source and one username each get per minute.</summary>
    internal int AttemptsPerMinute { get; set; }

    /// <summary>Gets or sets how many password verifications the surface may have in flight at once.</summary>
    internal int MaxConcurrentVerifications { get; set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the scheme was registered without a surface or without a bound, either of which would leave an admitted request carrying no identity, an unbounded number of guesses, or no verification admitted at all.</exception>
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

        if (this.MaxConcurrentVerifications <= 0)
        {
            throw new InvalidOperationException(
                "The Basic authentication scheme was registered without a positive verification ceiling, which would "
                + "refuse every request rather than bounding any.");
        }
    }
}
