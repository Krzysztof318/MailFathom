// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.AI.Providers;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Providers;

/// <summary>Declares the non-interactive Microsoft Entra credential an AI provider endpoint is reached with, where no key exists to provision.</summary>
/// <remarks>
/// <para>
/// Present only for a deployment whose endpoint accepts Microsoft Entra, and absent by default rather than an empty
/// block: secret discovery walks the bound options graph by type, so an empty block left here would be discovered on
/// every key-authenticated endpoint and fail startup with an unresolvable reference nobody wrote.
/// </para>
/// <para>
/// The four shapes are the whole of what a background service may hold. An interactive credential has no way to
/// complete where nobody is at a keyboard, and a developer-tool credential would let a deployed service authenticate
/// as whoever last signed in on the host — which is why the chain is composed from these members rather than taken
/// from <c>DefaultAzureCredential</c>, whose own chain contains both.
/// </para>
/// </remarks>
internal sealed class ProviderEntraCredentialOptions
{
    /// <summary>Gets or sets which non-interactive shape the deployment holds.</summary>
    /// <remarks>The two members naming something other than a Microsoft Entra credential are rejected here: a key is declared as one in the endpoint's own key block, and an endpoint needing no credential declares that on the endpoint rather than inside a credential.</remarks>
    public ProviderEndpointCredentialKind Kind { get; set; } = ProviderEndpointCredentialKind.ManagedIdentity;

    /// <summary>Gets or sets the scope an access token is requested for.</summary>
    /// <remarks>
    /// Declared rather than derived from the endpoint address, because it is the audience a token is minted for and
    /// the value has changed as the service was renamed. Deriving it would mint tokens for the wrong audience the next
    /// time it moves, and that arrives as an authentication failure with no setting to point at.
    /// </remarks>
    [Required]
    public string TokenScope { get; set; } = "https://ai.azure.com/.default";

    /// <summary>Gets or sets the directory the application is registered in, where the shape does not read it from the platform.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the application or user-assigned identity being authenticated.</summary>
    /// <remarks>Left empty for a system-assigned managed identity, which a resource carries at most one of and which therefore needs no naming.</remarks>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the reference to the registered application's secret, for the client-secret shape alone.</summary>
    public ConfiguredSecret? ClientSecret { get; set; }

    /// <summary>Gets or sets the path of the registered application's PKCS#12 certificate, for the client-certificate shape alone.</summary>
    /// <remarks>A path rather than a secret reference, because the file is read by the process account and protected by the same least-privilege rules as every other credential material on the host.</remarks>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the reference to the password protecting that certificate, where it has one.</summary>
    public ConfiguredSecret? CertificatePassword { get; set; }

    /// <summary>Reports every reason this credential could not be built, by reading the declaration alone.</summary>
    /// <param name="endpointAlias">The endpoint this credential belongs to, so a report names it.</param>
    /// <returns>One result per rule this declaration breaks.</returns>
    public IEnumerable<ValidationResult> FindConfigurationErrors(string endpointAlias)
    {
        if (this.Kind is ProviderEndpointCredentialKind.ApiKey)
        {
            yield return Error(endpointAlias, "declares a Microsoft Entra credential of kind ApiKey. A provider key is declared in the endpoint's ApiKey block instead.");

            yield break;
        }

        if (this.Kind is ProviderEndpointCredentialKind.Unauthenticated)
        {
            yield return Error(endpointAlias, "declares a Microsoft Entra credential of kind Unauthenticated. An endpoint that asks for no credential declares Unauthenticated on the endpoint and no credential block at all.");

            yield break;
        }

        if (this.Kind is ProviderEndpointCredentialKind.ClientSecret or ProviderEndpointCredentialKind.ClientCertificate)
        {
            if (this.TenantId.Length == 0)
            {
                yield return Error(endpointAlias, $"declares a {this.Kind} credential and no TenantId.");
            }

            if (this.ClientId.Length == 0)
            {
                yield return Error(endpointAlias, $"declares a {this.Kind} credential and no ClientId.");
            }
        }

        if (this.Kind is ProviderEndpointCredentialKind.ClientSecret && this.ClientSecret is null)
        {
            yield return Error(endpointAlias, "declares a ClientSecret credential and references no secret.");
        }

        if (this.Kind is ProviderEndpointCredentialKind.ClientCertificate && this.CertificatePath.Length == 0)
        {
            yield return Error(endpointAlias, "declares a ClientCertificate credential and no CertificatePath.");
        }

        if (this.TokenScope.Trim().Length == 0)
        {
            yield return Error(endpointAlias, "declares a Microsoft Entra credential and no TokenScope, so no token audience is known.");
        }
    }

    private static ValidationResult Error(string endpointAlias, string detail) =>
        new($"AI endpoint '{endpointAlias}' {detail}");
}
