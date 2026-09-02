// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Credentials;

/// <summary>What comparing a presented password against a stored hash established.</summary>
/// <remarks>
/// <para>
/// Three outcomes rather than a boolean, because the third is the whole of what makes an adaptive hash adaptive. Work
/// parameters are raised over the life of a deployment as hardware gets faster, and a hash written under the old ones
/// stays valid: the only moment the deployment holds the plaintext needed to write a stronger one is the request that
/// just verified it, so a verification that succeeded has to be able to say the record is behind.
/// </para>
/// <para>
/// Nothing here distinguishes one failure from another. A hash that will not parse, a hash written under an algorithm
/// this release no longer implements, and a password that is simply wrong are all <see cref="Failed" />, because the
/// caller answers all three with the same refusal and a vocabulary that told them apart would invite one of them to be
/// reported.
/// </para>
/// </remarks>
public enum PasswordVerification
{
    /// <summary>The presented password does not match the stored hash, or the stored hash cannot be read at all.</summary>
    Failed = 0,

    /// <summary>The presented password matches, and the stored hash is already written under the current policy.</summary>
    Succeeded = 1,

    /// <summary>The presented password matches a hash written under weaker parameters than the current policy, which the caller may replace while it still holds the plaintext.</summary>
    SucceededAndShouldBeRehashed = 2,
}
