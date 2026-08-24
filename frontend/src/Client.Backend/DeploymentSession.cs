// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend;

/// <summary>What a deployment reports back about the caller presenting a credential to it.</summary>
/// <param name="Service">The product that answered, so the client can tell it reached MailFathom rather than something else on the port.</param>
/// <param name="Version">The version the deployment is running.</param>
/// <param name="Permissions">The published names of what this caller's grant carries, empty for a caller granted nothing.</param>
/// <remarks>
/// <para>
/// The client's own record for the wire shape rather than a type shared with the service. The two ends state the same
/// contract, and coupling them would put a service type behind a screen — which is the rule
/// <c>frontend/src/AGENTS.md</c> § <em>Reaching the backend</em> states.
/// </para>
/// <para>
/// Three fields and no fourth, because that is what the route answers. It names no credential at all — not the
/// material, and not the deployment's own configured name for whatever authenticated — so nothing here can identify
/// one, and a client that modelled a name would be modelling a field it never receives.
/// <c>docs/operations/client-endpoint.md</c> § <em>What it serves</em> is the other end of this.
/// </para>
/// <para>
/// What does come back is a grant the caller could have derived by trying every route, which is why the route behind
/// it requires no permission at either end.
/// </para>
/// </remarks>
public sealed record DeploymentSession(
    string Service,
    string Version,
    IReadOnlyList<string> Permissions)
{
    /// <summary>Reports whether this caller's grant carries a published permission name.</summary>
    /// <param name="permission">The published name, as <c>docs/operations/permissions.md</c> writes it.</param>
    /// <returns><see langword="true" /> where the deployment named it, otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="permission" /> is blank.</exception>
    /// <remarks>
    /// An ordinal comparison, because these are protocol tokens rather than words: the published set is lowercase and
    /// dot-separated by rule, and a culture-aware comparison would make what a client offers depend on the language it
    /// is being read in. A document that named no grant at all is read as a caller granted nothing, which is the same
    /// answer an empty list gives and the safe one either way.
    /// </remarks>
    public bool Grants(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return this.Permissions is { } granted && granted.Contains(permission, StringComparer.Ordinal);
    }
}
