// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Observability;

/// <summary>Records that a caller reached something its grant does not carry, which is the only place that boundary is visible.</summary>
/// <remarks>
/// <para>
/// A refusal on the MCP surface tells the caller nothing — a tool it may not reach is absent from the listing, and a
/// call naming one is answered exactly as a call naming a tool that does not exist — so an operator diagnosing a client
/// that stopped working has this record or nothing at all. On the administrative surface the caller is told the
/// permission that would have sufficed, and the record is still what turns one operator's refusal into a rate somebody
/// can alert on: a credential that starts asking for what it was never granted is the signal, rather than any single
/// failure.
/// </para>
/// <para>
/// What counts as a refusal is a call or a route refused for want of a permission. A tool omitted from a listing is not
/// one: nothing was refused, every narrowed caller would report one on every listing, and the omission has no operation
/// to partition by — so the thing worth alerting on would sit under the steady state.
/// </para>
/// <para>
/// The operation a refusal names is MailFathom's own name for what was refused and never a value a caller chose. A
/// dimension whose values a caller picks is a time series per value, so a boundary reduces a name that arrived on a
/// request to one this repository publishes before it reaches here.
/// </para>
/// </remarks>
public interface IAuthorizationRefusalTelemetry
{
    /// <summary>Records one refusal, on both the counted channel and the one an operator reads.</summary>
    /// <param name="surface">The surface the refusal happened on.</param>
    /// <param name="operation">MailFathom's own name for the tool or the route that was refused.</param>
    /// <param name="requiredPermission">The permission that would have sufficed, unspecified where the refusal named none.</param>
    /// <param name="refusedIdentity">What the caller was admitted as, or <see langword="null" /> where the work was reached under no principal.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="operation" /> is empty or white space, which would leave the refusal nothing to be partitioned by.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    void RecordRefusal(
        ProtectedSurface surface,
        string operation,
        MailFathomPermission requiredPermission,
        string? refusedIdentity);
}
