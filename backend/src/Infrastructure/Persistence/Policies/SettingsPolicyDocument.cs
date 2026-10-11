// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>The settings policy one scope holds, and the version a change to it is composed over.</summary>
/// <param name="OrganizationId">The organization whose policy this is, or <see langword="null" /> for the deployment's own.</param>
/// <param name="Json">The policy, as the JSON object the row holds.</param>
/// <param name="Version">The version the policy was read at, which is <see cref="UnwrittenVersion" /> where the scope stores none.</param>
/// <remarks>
/// The version travels with the document for the reason a user's record carries its own: a writer that read the two
/// apart would state a version it read in a second query, which is the race the version exists to refuse.
/// </remarks>
public sealed record SettingsPolicyDocument(Guid? OrganizationId, string Json, long Version)
{
    /// <summary>The largest policy this build reads, and therefore the largest one it will persist.</summary>
    /// <remarks>
    /// The bound a user's record is held to, because a policy is made of the same material: two sparse documents in
    /// the shape of a user's record and two in the shape of a mail account's. One bound rather than two for the
    /// reason that record states — a write permitted past what a read accepts would persist a policy nobody could
    /// open again to correct.
    /// </remarks>
    public const int MaximumOctets = 1024 * 1024;

    /// <summary>The version a scope that stores no policy is read at, and the one its first write is composed over.</summary>
    public const long UnwrittenVersion = 0;

    /// <summary>The document a scope that stores no policy is read as: one that states nothing.</summary>
    public const string StatingNothing = "{}";

    /// <summary>Describes the policy of a scope that stores none.</summary>
    /// <param name="organizationId">The organization, or <see langword="null" /> for the deployment.</param>
    /// <returns>A policy stating nothing, at the version its first write is composed over.</returns>
    public static SettingsPolicyDocument Unwritten(Guid? organizationId) =>
        new(organizationId, StatingNothing, UnwrittenVersion);
}
