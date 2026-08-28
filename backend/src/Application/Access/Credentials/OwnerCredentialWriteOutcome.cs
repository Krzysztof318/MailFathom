// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Credentials;

/// <summary>What an administrative act on an owner's password credential did, or why it did nothing.</summary>
/// <remarks>
/// <para>
/// A result rather than an exception, because every value but the first is an operator's mistake in the request they
/// wrote — a mistyped owner, a username somebody already took, a credential deleted between reading a listing and
/// acting on it. The boundary answers each with a sentence naming what to write instead, which is not an exceptional
/// state and not something to unwind a call stack for.
/// </para>
/// <para>
/// Nothing here says whether a password was strong enough, because that is refused before the store is reached: the
/// policy is a property of what a deployment accepts rather than of what a row would allow, and refusing it beside a
/// missing owner would put the two in one vocabulary that the transport then has to split apart again.
/// </para>
/// </remarks>
public enum OwnerCredentialWriteOutcome
{
    /// <summary>The act was performed and is durable.</summary>
    Written = 0,

    /// <summary>This deployment holds no owner record with the named identity.</summary>
    UnknownOwner = 1,

    /// <summary>Another credential already carries the canonical username, which resolves one owner and cannot resolve two.</summary>
    UsernameTaken = 2,

    /// <summary>The named owner holds no credential with that identifier.</summary>
    UnknownCredential = 3,

    /// <summary>The owner already holds as many credentials as one owner may.</summary>
    /// <remarks>
    /// The ceiling is <see cref="OwnerPasswordCredential.MaximumListedPerOwner" />, and it is refused at the store
    /// rather than checked before the write, so two administrators provisioning at once cannot both pass a count and
    /// leave the owner above it. Reaching it is a deployment provisioning credentials it never removes rather than an
    /// operator's ordinary mistake, so what the refusal asks for is a look at what the owner already holds.
    /// </remarks>
    OwnerAtCredentialCeiling = 4,
}
