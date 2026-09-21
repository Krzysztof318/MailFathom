// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Secrets.Resolution;

/// <summary>Why one configured secret declares an identity or a lifetime the host cannot use.</summary>
/// <remarks>
/// <para>
/// The vocabulary is deliberately about the declaration rather than about the material, so every value is safe to
/// report to an operator alongside the configuration path that produced it. Nothing here names a reference target, a
/// retrieval outcome, or a secret; <see cref="SecretResolutionFailure" /> answers that separate question.
/// </para>
/// <para>
/// A repeated name is three values rather than one because a name that means one credential is what the rule protects,
/// and an operator correcting a repetition has to know which setting disagrees with the declaration that claimed the
/// name first. Two declarations that agree about all three are one credential declared twice and are accepted.
/// </para>
/// </remarks>
public enum SecretDeclarationFailure
{
    /// <summary>No name was configured, so the secret has no identity a rotation, an expiry, or an audit record could name.</summary>
    NameMissing = 0,

    /// <summary>A name was configured in a spelling <see cref="SecretName" /> does not accept.</summary>
    NameMalformed = 1,

    /// <summary>A name another declaration in the same configuration root already uses was given a different <see cref="Discovery.ConfiguredSecret.SecretReference" />, so it would name two credentials.</summary>
    NameReusedForAnotherReference = 2,

    /// <summary>The lifetime is blank, which is a mistake rather than a second spelling of <see cref="SecretLifetime.NoLimitValue" />.</summary>
    LifetimeMissing = 3,

    /// <summary>The lifetime is neither <see cref="SecretLifetime.NoLimitValue" /> nor an ISO 8601 instant carrying an explicit offset.</summary>
    LifetimeMalformed = 4,

    /// <summary>A name another declaration in the same configuration root already uses was given a different <see cref="Discovery.ConfiguredSecret.Lifetime" />, so one credential would carry two expiries.</summary>
    NameReusedForAnotherLifetime = 5,

    /// <summary>A name another declaration in the same configuration root already uses names a different <see cref="Discovery.ConfiguredSecret.Password" />, or one where the other names none, so the material behind it would be unsealed two ways.</summary>
    NameReusedForAnotherPassword = 6,
}
