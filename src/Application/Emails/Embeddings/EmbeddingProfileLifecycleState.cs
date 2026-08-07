// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>Where one embedding profile is in its life, which is the whole of what a registered profile may change.</summary>
/// <remarks>
/// A profile's identity is fixed at insertion, so this is the only thing about a profile that moves. There is no
/// generation counter beside it: the profile is the generation, so two generations coexisting while a new one is built
/// are two rows, one <see cref="Building" /> and one <see cref="Active" />, and a read path has no second field it must
/// remember to consult. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>.
/// </remarks>
public enum EmbeddingProfileLifecycleState
{
    /// <summary>Registered and being embedded into, and never read from while it is here.</summary>
    Building = 0,

    /// <summary>The one profile retrieval reads. At most one profile is here at a time.</summary>
    Active = 1,

    /// <summary>Replaced by a later generation, and whose vectors are being removed in bounded batches.</summary>
    Superseded = 2,
}
