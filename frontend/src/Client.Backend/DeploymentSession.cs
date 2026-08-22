// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend;

/// <summary>What a deployment reports back about the caller presenting a credential to it.</summary>
/// <param name="Service">The product that answered, so the client can tell it reached MailFathom rather than something else on the port.</param>
/// <param name="Version">The version the deployment is running.</param>
/// <param name="Credential">The deployment's own configured name for whatever authenticated, or <c>anonymous</c>.</param>
/// <param name="Permissions">The published names of what this caller's grant carries, empty for a caller granted nothing.</param>
/// <remarks>
/// <para>
/// The client's own record for the wire shape rather than a type shared with the service. The two ends state the same
/// contract, and coupling them would put a service type behind a screen — which is the rule
/// <c>frontend/src/AGENTS.md</c> § <em>Reaching the backend</em> states.
/// </para>
/// <para>
/// Nothing here identifies the credential's material, because the deployment does not report it. What comes back is a
/// name an operator configured and a grant the caller could have derived by trying every route, which is why the route
/// behind it requires no permission at either end.
/// </para>
/// </remarks>
public sealed record DeploymentSession(
    string Service,
    string Version,
    string Credential,
    IReadOnlyList<string> Permissions);
