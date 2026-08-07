// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Embeddings;

/// <summary>Names how a deployment proves its identity to one embedding endpoint.</summary>
/// <remarks>
/// Every member is non-interactive, and that is the whole of the set rather than a subset of a longer one. MailFathom
/// is a background service with nobody at a keyboard, so a credential that opens a browser or prints a device code has
/// no way to complete and would surface as a request that never returns. That is also why the Microsoft Entra chain is
/// composed from these members explicitly rather than taken from <c>DefaultAzureCredential</c>, whose chain contains
/// both those shapes and the developer-tool credentials besides.
/// </remarks>
public enum EmbeddingEndpointCredentialKind
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
}
