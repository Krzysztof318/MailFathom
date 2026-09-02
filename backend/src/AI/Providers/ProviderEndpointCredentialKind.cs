// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Providers;

/// <summary>Names how a deployment proves its identity to one AI provider endpoint.</summary>
/// <remarks>
/// <para>
/// One set for every provider role rather than one per role. An endpoint that serves embeddings and an endpoint that
/// serves generation are reached through the same client library, at the same kind of address, with the same shapes of
/// credential, so a second copy of this set would be two enumerations that had to be kept identical by hand.
/// </para>
/// <para>
/// Every member is non-interactive, and that is the whole of the set rather than a subset of a longer one. MailFathom
/// is a background service with nobody at a keyboard, so a credential that opens a browser or prints a device code has
/// no way to complete and would surface as a request that never returns. That is also why the Microsoft Entra chain is
/// composed from these members explicitly rather than taken from <c>DefaultAzureCredential</c>, whose chain contains
/// both those shapes and the developer-tool credentials besides.
/// </para>
/// </remarks>
public enum ProviderEndpointCredentialKind
{
    /// <summary>A key the provider issued, carried as a secret reference and resolved per request.</summary>
    ApiKey = 0,

    /// <summary>The managed identity assigned to the Azure resource the service runs on.</summary>
    /// <remarks>The shape with no secret at all, which is why it is preferred wherever the deployment can hold one.</remarks>
    ManagedIdentity = 1,

    /// <summary>The federated workload identity a Kubernetes service account is annotated with.</summary>
    /// <remarks>Also holds no secret: the projected service-account token is exchanged for an access token, and the projection is the platform's to rotate.</remarks>
    WorkloadIdentity = 2,

    /// <summary>A registered application authenticating with its client secret.</summary>
    ClientSecret = 3,

    /// <summary>A registered application authenticating with its certificate.</summary>
    ClientCertificate = 4,

    /// <summary>Nothing at all: the endpoint asks for no credential, and the request carries none.</summary>
    /// <remarks>
    /// The ordinary shape of a model server the operator runs themselves, which admits a caller by being reachable only
    /// from the network it was put on. It is a member of this set rather than an absence outside it, because an
    /// endpoint that needs no credential has to be able to say so: an omission is what a forgotten key reference looks
    /// like, and startup goes on refusing that.
    /// </remarks>
    Unauthenticated = 5,
}
