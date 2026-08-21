// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access;

/// <summary>Reports whoever the unit of work in hand is running for.</summary>
/// <remarks>
/// <para>
/// The one seam between the application layer and whatever admitted the work. An adapter outside this layer populates
/// it per unit of work — a host adapter from the authenticated principal of a request, a route from a capability it has
/// just verified — so that nothing here has to know what a request, a scheme, or a claim is.
/// </para>
/// <para>
/// <see langword="null" /> is a real answer and means the work was reached under none of the three principal kinds. It
/// is never read as permission: <see cref="AccessAuthorization" /> refuses it, so an entrypoint that forgets to state
/// what it admitted fails rather than being served.
/// </para>
/// <para>
/// An implementation is registered per unit of work, which for a served request is its scope. A use case asks
/// <see cref="AccessAuthorization" /> rather than this port directly.
/// </para>
/// </remarks>
public interface IAuthorizedPrincipalSource
{
    /// <summary>Gets the principal this unit of work is running for, or <see langword="null" /> when it was reached under none.</summary>
    AuthorizedPrincipal? Current { get; }
}
