// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Providers;

/// <summary>Everything one non-interactive Microsoft Entra credential is built from.</summary>
/// <param name="Kind">Which of the four non-interactive shapes the deployment holds.</param>
/// <param name="TokenScope">The scope an access token is requested for.</param>
/// <param name="TenantId">The directory the application is registered in, or <see langword="null" /> where the shape reads it from the platform.</param>
/// <param name="ClientId">The application or user-assigned identity being authenticated, or <see langword="null" /> for a system-assigned managed identity.</param>
/// <param name="ClientSecret">The resolved application secret, for <see cref="ProviderEndpointCredentialKind.ClientSecret" /> alone.</param>
/// <param name="CertificatePath">The path of the application certificate, for <see cref="ProviderEndpointCredentialKind.ClientCertificate" /> alone.</param>
/// <param name="CertificatePassword">The resolved password protecting that certificate, or <see langword="null" /> where it needs none.</param>
/// <remarks>
/// <para>
/// A record of resolved values rather than of references, because resolving a reference is the host's business and
/// this boundary holds no secret provider. What arrives here is what a credential is constructed with and nothing
/// more.
/// </para>
/// <para>
/// The scope is declared rather than derived, because it is the audience an access token is minted for and the value
/// has changed as the service was renamed. Deriving it from an endpoint address would silently mint tokens for the
/// wrong audience the next time it moves, which arrives as an authentication failure with no configuration to point at.
/// </para>
/// </remarks>
public sealed record EntraCredentialDeclaration(
    ProviderEndpointCredentialKind Kind,
    string TokenScope,
    string? TenantId,
    string? ClientId,
    string? ClientSecret,
    string? CertificatePath,
    string? CertificatePassword)
{
    /// <inheritdoc />
    /// <remarks>
    /// The synthesized record printing is replaced because two members are complete secrets, and a record renders every
    /// member it holds into every log line, exception message, and diagnostic dump the value reaches.
    /// </remarks>
    public override string ToString() => $"{nameof(EntraCredentialDeclaration)} {{ Kind = {this.Kind} }}";
}
