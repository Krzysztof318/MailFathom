// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>What this deployment's stored mail content may occupy in total, and what any one user may occupy of it.</summary>
/// <param name="DeploymentBytes">The deployment's ceiling, or <see langword="null" /> where storage is bounded only by the disk.</param>
/// <param name="UserBytes">The per-user ceiling, or <see langword="null" /> where no user is bounded separately.</param>
/// <remarks>
/// <para>
/// The two travel together because a claim is admitted by both or by neither, and an implementation that took them one
/// at a time would have to decide what to do about the half it had already charged. They are also counted in different
/// quantities on purpose: the deployment's is what the operator's disk fills with, which only the database can report,
/// and a user's is what their payloads hold, which is the only figure attributable to one person at all.
/// </para>
/// <para>
/// An absent ceiling is not zero and not <see cref="long.MaxValue" /> written out — it is the statement that nothing
/// bounds that population, which is what lets a deployment that declared neither take no claim at all.
/// </para>
/// </remarks>
public readonly record struct StoredContentCeilings(long? DeploymentBytes, long? UserBytes)
{
    /// <summary>Gets whether either population is bounded, which is what decides whether a claim is worth taking.</summary>
    public bool BoundsAnything => this.DeploymentBytes.HasValue || this.UserBytes.HasValue;
}
