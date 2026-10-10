// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Organizations;

/// <summary>What a deployment administrator does to organizations and to which organization a user or a mail account belongs.</summary>
/// <remarks>
/// <para>
/// Reading is <see cref="MailFathomPermission.AdminRead" />. Recording, renaming, and deleting an organization, and moving
/// a mail account, is <see cref="MailFathomPermission.AdminConfigurationWrite" />, the grant that already records users
/// and assigns accounts, because those change how the deployment's people and mailboxes are grouped rather than how any
/// of them signs in. Changing an organization's short
/// name and moving a user are <see cref="MailFathomPermission.AdminCredentialsWrite" />, because each changes the login a
/// password is typed as — a short name every member's at once — which is a decision about how people sign in.
/// </para>
/// <para>
/// Which scope reaches each act is ADR 0012's. Recording and deleting an organization and changing its short name are
/// the deployment's alone — a short name is a namespace every organization's logins share, so a refusal on a collision
/// would tell one organization's administrator what another holds. Reading, renaming the display name, and moving a
/// user are admitted at a scope covering what they name, and what it does not cover is answered as absent.
/// </para>
/// <para>
/// The identifier is a version 7 value minted from the instant the organization is recorded at, like every identifier
/// MailFathom mints. It reaches administrative listings, so it says when each company was added and in what order — a
/// residual ADR 0036 accepts.
/// </para>
/// </remarks>
public sealed class OrganizationAdministration
{
    private readonly AccessAuthorization authorization;
    private readonly IOrganizationStore organizations;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the administration over one deployment's organizations.</summary>
    /// <param name="authorization">What each act is admitted by.</param>
    /// <param name="organizations">Where the organizations are kept.</param>
    /// <param name="timeProvider">The clock a record is stamped with.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OrganizationAdministration(
        AccessAuthorization authorization,
        IOrganizationStore organizations,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(organizations);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.organizations = organizations;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads one page of the organizations the caller's scope covers.</summary>
    /// <param name="query">The page asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page's organizations in identifier order, beside the rows on it this build will not read as one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRead" /> at no scope.</exception>
    /// <remarks>A listing never refuses over a scope: a caller reading at one organization's scope lists that organization, and one reading at a user's scope alone lists none.</remarks>
    public Task<OrganizationListing> ReadAsync(AdministrativeListingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        return this.organizations.ReadAsync(
            query,
            this.authorization.ScopesOf(MailFathomPermission.AdminRead),
            cancellationToken);
    }

    /// <summary>Reads every organization row the caller's scope covers that this build will not read as one.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The unreadable rows, each with the sentence naming what its short name must become.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRead" /> at no scope.</exception>
    public Task<IReadOnlyList<UnreadableOrganization>> ReadUnreadableAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        return this.organizations.ReadUnreadableAsync(
            this.authorization.ScopesOf(MailFathomPermission.AdminRead),
            cancellationToken);
    }

    /// <summary>Records an organization under an identifier this deployment mints.</summary>
    /// <param name="displayName">The name an operator reads it by.</param>
    /// <param name="shortName">The short name its members will sign in under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, carrying the minted identifier when it was written.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="displayName" /> breaks <see cref="FindDisplayNameRefusal" /> or <paramref name="shortName" /> is the unspecified struct default.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    public async Task<OrganizationWriteResult> CreateAsync(
        string? displayName,
        OrganizationShortName shortName,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        var label = DisplayNameOrThrow(displayName);
        var createdAt = this.timeProvider.GetUtcNow();
        var organizationId = Guid.CreateVersion7(createdAt);

        var result = await this.organizations.CreateAsync(
            organizationId,
            label,
            RequireShortName(shortName),
            createdAt,
            cancellationToken);

        return result with { OrganizationId = organizationId };
    }

    /// <summary>Replaces the name an operator reads an organization by.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="displayName">The new display name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="displayName" /> breaks <see cref="FindDisplayNameRefusal" />, or <paramref name="organizationId" /> is empty.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminConfigurationWrite" /> at no scope.</exception>
    /// <remarks>The display name is the one part of an organization its own administrator renames; an organization outside the caller's scope is answered as one this deployment does not hold.</remarks>
    public async Task<OrganizationWriteResult> RenameAsync(
        Guid organizationId,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var label = DisplayNameOrThrow(displayName);
        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminConfigurationWrite);

        return await this.CoversOrganizationAsync(MailFathomPermission.AdminConfigurationWrite, organizationId, cancellationToken)
            ? await this.organizations.RenameAsync(organizationId, label, cancellationToken)
            : OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownOrganization);
    }

    /// <summary>Replaces the short name an organization's members sign in under, which moves every one of their logins with it.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="shortName">The new short name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="shortName" /> is the unspecified struct default.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    public Task<OrganizationWriteResult> ChangeShortNameAsync(
        Guid organizationId,
        OrganizationShortName shortName,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        return this.organizations.ChangeShortNameAsync(organizationId, RequireShortName(shortName), cancellationToken);
    }

    /// <summary>Removes an organization, which is refused while it still has members or holds mail accounts.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, carrying how many members and mail accounts refused it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    public Task<OrganizationWriteResult> DeleteAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        return this.organizations.DeleteAsync(organizationId, cancellationToken);
    }

    /// <summary>Moves a user into an organization, or out of every organization.</summary>
    /// <param name="user">The user being moved.</param>
    /// <param name="organizationId">The organization to move them into, or <see langword="null" /> to leave them in none.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, carrying the colliding username where the target already holds one of theirs.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody, or <paramref name="organizationId" /> is empty.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminCredentialsWrite" /> at no scope, or moves a user out of every organization without holding it over the whole deployment.</exception>
    /// <remarks>
    /// Both sides have to be covered: the user where they stand now, and the organization they are moved into. Leaving
    /// every organization is a move into what only the deployment's scope covers, so only the deployment's scope writes
    /// it, and it is refused by name because it names no organization whose existence a refusal could disclose. A user
    /// outside the caller's scope is answered as unknown and an organization outside it as unknown, each the way the
    /// write answers one that does not exist.
    /// </remarks>
    public async Task<OrganizationWriteResult> SetUserOrganizationAsync(
        UserId user,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user is moved between organizations by name.", nameof(user));
        }

        if (organizationId is not { } destination)
        {
            this.authorization.RequirePermissionOverTheDeployment(MailFathomPermission.AdminCredentialsWrite);

            return await this.organizations.SetUserOrganizationAsync(user, organizationId, cancellationToken);
        }

        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminCredentialsWrite);

        if (!await this.authorization.PermitsOverAsync(MailFathomPermission.AdminCredentialsWrite, user, cancellationToken))
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownUser);
        }

        if (!await this.CoversOrganizationAsync(MailFathomPermission.AdminCredentialsWrite, destination, cancellationToken))
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownOrganization);
        }

        return await this.organizations.SetUserOrganizationAsync(user, organizationId, cancellationToken);
    }

    /// <summary>Asks whether the caller holds a permission at a scope covering one organization itself.</summary>
    private Task<bool> CoversOrganizationAsync(
        MailFathomPermission permission,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        this.authorization.PermitsOverScopeAsync(permission, AssignmentScope.Organization(organizationId), cancellationToken);

    /// <summary>Moves a mail account into an organization, or out of every organization.</summary>
    /// <param name="mailAccountId">The mail account being moved.</param>
    /// <param name="organizationId">The organization to move it into, or <see langword="null" /> to leave it in none.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, carrying how many of its users stand outside the target where that refused it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    public Task<OrganizationWriteResult> SetMailAccountOrganizationAsync(
        Guid mailAccountId,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        return this.organizations.SetMailAccountOrganizationAsync(mailAccountId, organizationId, cancellationToken);
    }

    /// <summary>Reports why a written display name cannot be an organization's, or that it can.</summary>
    /// <param name="displayName">The display name as it was written.</param>
    /// <returns>The sentence naming what to write instead, or <see langword="null" /> when it is accepted.</returns>
    public static string? FindDisplayNameRefusal(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "An organization is recorded with the name an operator reads it by. Write one.";
        }

        var trimmed = displayName.Trim();

        return trimmed.Length > Organization.MaximumDisplayNameLength
            ? $"The display name is {trimmed.Length} characters, past the {Organization.MaximumDisplayNameLength} an organization's name is stored as. Shorten it."
            : null;
    }

    private static string DisplayNameOrThrow(string? displayName) =>
        FindDisplayNameRefusal(displayName) is { } refusal
            ? throw new ArgumentException(
                $"The display name was not checked before it reached the use case. {refusal}",
                nameof(displayName))
            : displayName!.Trim();

    private static OrganizationShortName RequireShortName(OrganizationShortName shortName) =>
        shortName.IsSpecified
            ? shortName
            : throw new ArgumentException(OrganizationShortName.DescribeAcceptedForm(), nameof(shortName));
}
