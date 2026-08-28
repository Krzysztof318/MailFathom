// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Authentication;

namespace MailFathom.Host.Security.ApiKeys;

/// <summary>Which surface an owner-facing API key scheme protects.</summary>
/// <remarks>
/// There is no key list here and no grant, unlike the configured scheme's options, and their absence is the method: the
/// keys are rows in the deployment's own database rather than material an operator wrote into a section, and what each
/// one grants is recorded beside the owner it resolves. What is left for the scheme to carry is the surface, which
/// names the identity a success reports itself under — and which is what lets two surfaces register one handler.
/// </remarks>
internal sealed class OwnerApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    /// <summary>Gets or sets the surface this scheme protects, which names the claim identity a success produces.</summary>
    internal TransportSurface Surface { get; set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the scheme was registered without a surface, which would leave a successful authentication carrying no identity.</exception>
    public override void Validate()
    {
        base.Validate();

        if (!this.Surface.IsSpecified)
        {
            throw new InvalidOperationException(
                "The owner API key authentication scheme was registered without a transport surface.");
        }
    }
}
