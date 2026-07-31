// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Why one configured secret declares an identity or a lifetime the host cannot use.</summary>
/// <remarks>
/// The vocabulary is deliberately about the declaration rather than about the material, so every value is safe to
/// report to an operator alongside the configuration path that produced it. Nothing here names a reference target, a
/// retrieval outcome, or a secret; <see cref="SecretResolutionFailure" /> answers that separate question.
/// </remarks>
public enum SecretDeclarationFailure
{
    /// <summary>No name was configured, so the secret has no identity a rotation, an expiry, or an audit record could name.</summary>
    NameMissing = 0,

    /// <summary>A name was configured in a spelling <see cref="SecretName" /> does not accept.</summary>
    NameMalformed = 1,

    /// <summary>A name was configured that another secret in the same configuration root already uses.</summary>
    NameDuplicated = 2,

    /// <summary>The lifetime is blank, which is a mistake rather than a second spelling of <see cref="SecretLifetime.NoLimitValue" />.</summary>
    LifetimeMissing = 3,

    /// <summary>The lifetime is neither <see cref="SecretLifetime.NoLimitValue" /> nor an ISO 8601 instant carrying an explicit offset.</summary>
    LifetimeMalformed = 4,
}
