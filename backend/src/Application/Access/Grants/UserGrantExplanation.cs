// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>Why one user holds each permission they hold within the reader's own scope, and what each of their credentials keeps of it.</summary>
/// <param name="User">The user.</param>
/// <param name="Sources">One row per permission, entry of a role's list granting it, and assignment, in the order the published set declares the permissions.</param>
/// <param name="SourcesTruncated">Whether the user holds more rows within the reader's scope than <see cref="MaximumSources" />, so <paramref name="Sources" /> is a part of the answer.</param>
/// <param name="Credentials">The user's credentials, each with the narrowing it applies, oldest first.</param>
/// <remarks>
/// The rows are the ones the reader could list as assignments: what the user holds through a group or at a scope the
/// reader's grant does not cover is left out, so an organization's administrator explaining a member reads that
/// organization's part of the member's grant and nothing of the deployment's. <see cref="Grant" /> is therefore that
/// part, and what a credential is shown to keep is what it keeps of that part.
/// </remarks>
public sealed record UserGrantExplanation(
    UserId User,
    IReadOnlyList<GrantSource> Sources,
    bool SourcesTruncated,
    IReadOnlyList<UserCredential> Credentials)
{
    /// <summary>The most rows one explanation carries.</summary>
    /// <remarks>A user holds one row per name each entry of a role's list grants per assignment, so reaching it takes dozens of assignments for one person, which is a provisioning mistake to find rather than a page to turn.</remarks>
    public const int MaximumSources = 1000;

    /// <summary>Gets the part of the user's grant the rows compose.</summary>
    public ScopedGrant Grant => ScopedGrant.Of(this.Sources.Select(source => (source.Permission, source.Scope)));
}
