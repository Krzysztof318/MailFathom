// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Organizations;

/// <summary>Where the organizations a deployment groups its users into are kept, and which one each user belongs to.</summary>
/// <remarks>
/// <para>
/// Every write answers an outcome rather than raising for the refusals an administrator meets in ordinary use — a short
/// name another organization holds, an organization that still has members, a username the target organization already
/// holds — because each is decided by the database at the moment of the write rather than by a read a moment earlier.
/// </para>
/// <para>
/// A password credential names the organization it is scoped to by identifier, so a short name changes without rewriting
/// any credential, and moving a user re-scopes their password credentials in the same transaction that moves them.
/// </para>
/// </remarks>
public interface IOrganizationStore
{
    /// <summary>Reads the organizations this deployment holds, ordered by short name.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>At most <see cref="Organization.MaximumListed" /> rows, each readable one with how many members it has, and each unreadable one named apart.</returns>
    /// <remarks>A row this build will not read is reported beside the listing rather than raised through it: one organization nobody can repair must not be every organization nobody can list.</remarks>
    Task<OrganizationListing> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Records an organization.</summary>
    /// <param name="organizationId">The identifier the organization is to carry.</param>
    /// <param name="displayName">The name an operator reads it by, already judged.</param>
    /// <param name="shortName">The short name its members will sign in under.</param>
    /// <param name="createdAt">When it was recorded.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="OrganizationWriteOutcome.Written" />, <see cref="OrganizationWriteOutcome.ShortNameTaken" />, or <see cref="OrganizationWriteOutcome.OrganizationCeilingReached" />.</returns>
    Task<OrganizationWriteResult> CreateAsync(
        Guid organizationId,
        string displayName,
        OrganizationShortName shortName,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    /// <summary>Replaces the name an operator reads an organization by.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="displayName">The new display name, already judged.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="OrganizationWriteOutcome.Written" /> or <see cref="OrganizationWriteOutcome.UnknownOrganization" />.</returns>
    Task<OrganizationWriteResult> RenameAsync(Guid organizationId, string displayName, CancellationToken cancellationToken);

    /// <summary>Replaces the short name an organization's members sign in under.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="shortName">The new short name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="OrganizationWriteOutcome.Written" />, <see cref="OrganizationWriteOutcome.UnknownOrganization" />, or <see cref="OrganizationWriteOutcome.ShortNameTaken" />.</returns>
    /// <remarks>No credential is rewritten: each names the organization by identifier, so every member's password works under the new short name from the moment this commits.</remarks>
    Task<OrganizationWriteResult> ChangeShortNameAsync(
        Guid organizationId,
        OrganizationShortName shortName,
        CancellationToken cancellationToken);

    /// <summary>Removes an organization that has no members.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="OrganizationWriteOutcome.Written" />, <see cref="OrganizationWriteOutcome.UnknownOrganization" />, or <see cref="OrganizationWriteOutcome.StillHasMembers" /> carrying how many.</returns>
    Task<OrganizationWriteResult> DeleteAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Moves a user into an organization or out of every organization, re-scoping their password credentials with them.</summary>
    /// <param name="user">The user being moved.</param>
    /// <param name="organizationId">The organization to move them into, or <see langword="null" /> to leave them in none.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="OrganizationWriteOutcome.Written" />, <see cref="OrganizationWriteOutcome.UnknownUser" />, <see cref="OrganizationWriteOutcome.UnknownOrganization" />, or <see cref="OrganizationWriteOutcome.UsernameTaken" /> carrying the colliding username where it could be read.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    Task<OrganizationWriteResult> SetUserOrganizationAsync(
        MailUserId user,
        Guid? organizationId,
        CancellationToken cancellationToken);
}
