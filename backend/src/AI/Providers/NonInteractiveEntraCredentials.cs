// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;

namespace MailFathom.AI.Providers;

/// <summary>Builds the Microsoft Entra credential a background service is allowed to hold.</summary>
/// <remarks>
/// <para>
/// The chain is composed here, member by member, rather than taken from <c>DefaultAzureCredential</c>. That default
/// chains an interactive browser credential and the developer-tool credentials of whoever is signed in on the machine,
/// and both are wrong for this process in different ways: the first has no way to complete where nobody is at a
/// keyboard and surfaces as a request that never returns, and the second would let a deployed service authenticate as
/// an operator's own account because a stale sign-in happened to be on the host.
/// </para>
/// <para>
/// So MailFathom names its four shapes and reaches nothing else. Two of them hold no secret at all and are what a
/// deployment on Azure or on Kubernetes should use; the other two exist for a deployment that is neither.
/// </para>
/// </remarks>
internal static class NonInteractiveEntraCredentials
{
    /// <summary>Builds the credential one endpoint's declaration describes.</summary>
    /// <param name="declaration">What the credential is built from.</param>
    /// <returns>The credential, which caches the access tokens it fetches.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaration" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the declaration names an API key rather than a Microsoft Entra shape.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the declaration is missing a value its own shape requires.</exception>
    public static TokenCredential Create(EntraCredentialDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration.Kind switch
        {
            ProviderEndpointCredentialKind.ManagedIdentity => CreateManagedIdentityCredential(declaration),
            ProviderEndpointCredentialKind.WorkloadIdentity => new WorkloadIdentityCredential(
                new WorkloadIdentityCredentialOptions
                {
                    TenantId = declaration.TenantId,
                    ClientId = declaration.ClientId,
                }),
            ProviderEndpointCredentialKind.ClientSecret => new ClientSecretCredential(
                Require(declaration.TenantId, nameof(EntraCredentialDeclaration.TenantId)),
                Require(declaration.ClientId, nameof(EntraCredentialDeclaration.ClientId)),
                Require(declaration.ClientSecret, nameof(EntraCredentialDeclaration.ClientSecret))),
            ProviderEndpointCredentialKind.ClientCertificate => CreateCertificateCredential(declaration),
            _ => throw new ArgumentOutOfRangeException(
                nameof(declaration),
                declaration.Kind,
                "An API key is presented directly rather than through a Microsoft Entra credential."),
        };
    }

    /// <summary>Builds a managed-identity credential, letting an absent client identifier select the system-assigned identity.</summary>
    /// <remarks>
    /// The identifier is optional here and required nowhere else, because a resource carries at most one
    /// system-assigned identity and any number of user-assigned ones. Naming none is therefore unambiguous, and
    /// naming one is how a resource with several says which.
    /// </remarks>
    private static ManagedIdentityCredential CreateManagedIdentityCredential(EntraCredentialDeclaration declaration) =>
        declaration.ClientId is { Length: > 0 } clientId
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(new ResourceIdentifier(clientId)))
            : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);

    /// <summary>Loads the application certificate and builds a credential from it.</summary>
    /// <remarks>
    /// The certificate is read from the file system by the process account, so the file is the deployment's to protect
    /// with the same least-privilege rules every other credential material carries. The password, where the file has
    /// one, arrives already resolved from a secret reference rather than as configuration text.
    /// </remarks>
    private static ClientCertificateCredential CreateCertificateCredential(EntraCredentialDeclaration declaration)
    {
        var certificatePath = Require(declaration.CertificatePath, nameof(EntraCredentialDeclaration.CertificatePath));
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, declaration.CertificatePassword);

        return new ClientCertificateCredential(
            Require(declaration.TenantId, nameof(EntraCredentialDeclaration.TenantId)),
            Require(declaration.ClientId, nameof(EntraCredentialDeclaration.ClientId)),
            certificate);
    }

    private static string Require(string? value, string memberName) => value is { Length: > 0 }
        ? value
        : throw new InvalidOperationException(
            $"A Microsoft Entra credential of this shape requires {memberName}, and the declaration carries none.");
}
