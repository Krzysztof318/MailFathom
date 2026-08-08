// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Search;

/// <summary>Says what semantic retrieval can do for this instance right now.</summary>
/// <remarks>
/// <para>
/// Three states, and the profile is what separates the first from the other two. An instance that has activated none
/// does not embed at all, which
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// makes a supported deployment rather than an unfinished one; an instance that has activated one either can place a
/// query in that profile's space or currently cannot.
/// </para>
/// <para>
/// It exists because a search that quietly returns less is worse than one that says it is degraded. Without it an
/// expired credential and a deliberately lexical deployment both answer with a lexical ranking and are
/// indistinguishable, so the operator whose key expired reads worse results instead of a state to fix.
/// </para>
/// <para>
/// It is read rather than probed. Nothing here calls a provider to establish it: what it reports is the outcome of the
/// calls the embedding workers already made, so no query pays for a health check and a provider that is refusing is not
/// asked again by every search that arrives while it refuses.
/// </para>
/// </remarks>
public enum SemanticSearchCapability
{
    /// <summary>This instance has activated no embedding profile, so nothing is ranked by meaning.</summary>
    /// <remarks>A supported deployment rather than a fault. Synchronization, listing, content retrieval, and lexical search are unaffected, and activating a declared profile is what changes it.</remarks>
    Inactive = 0,

    /// <summary>An embedding profile is active and its provider is answering, so a search is ranked both ways.</summary>
    /// <remarks>It says what the instance can do rather than what one call did: a query whose provider call fails is still answered, lexically, and reports the degraded state below.</remarks>
    Available = 1,

    /// <summary>An embedding profile is active but a query cannot be placed in its space, so searches are answered lexically until it recovers.</summary>
    /// <remarks>The credential was refused, the endpoint chain is unreachable, no provider is declared any more, or the declared model is not the one the active profile records. Every one of them needs an operator, and recovery is automatic once the cause is gone.</remarks>
    Degraded = 2,
}
