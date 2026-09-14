// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Organizations;

/// <summary>What a deployment administrator does to organizations and to which organization a user belongs.</summary>
/// <remarks>
/// <para>
/// Reading is <see cref="MailFathomPermission.AdminRead" />. Recording, renaming, and deleting an organization is
/// <see cref="MailFathomPermission.AdminConfigurationWrite" />, the grant that already records users, because those
/// change how the deployment's people are grouped rather than how any of them signs in. Changing an organization's short
/// name and moving a user are <see cref="MailFathomPermission.AdminCredentialsWrite" />, because each changes the login a
/// password is typed as — a short name every member's at once — which is a decision about how people sign in.
/// </para>
/// <para>
/// The identifier is a version 4 value for the reason a user's is: it reaches administrative listings, and a time-ordered
/// one would publish when each company was added and in what order.
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

    /// <summary>Reads the organizations this deployment holds.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The organizations, ordered by short name, beside the rows this build will not read as one.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminRead" />.</exception>
    public Task<OrganizationListing> ReadAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.organizations.ReadAsync(cancellationToken);
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
        var organizationId = Guid.NewGuid();

        var result = await this.organizations.CreateAsync(
            organizationId,
            label,
            RequireShortName(shortName),
            this.timeProvider.GetUtcNow(),
            cancellationToken);

        return result with { OrganizationId = organizationId };
    }

    /// <summary>Replaces the name an operator reads an organization by.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="displayName">The new display name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="displayName" /> breaks <see cref="FindDisplayNameRefusal" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    public Task<OrganizationWriteResult> RenameAsync(
        Guid organizationId,
        string? displayName,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        return this.organizations.RenameAsync(organizationId, DisplayNameOrThrow(displayName), cancellationToken);
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

    /// <summary>Removes an organization, which is refused while it still has members.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, carrying how many members refused it.</returns>
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
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    public Task<OrganizationWriteResult> SetUserOrganizationAsync(
        MailUserId user,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user is moved between organizations by name.", nameof(user));
        }

        return this.organizations.SetUserOrganizationAsync(user, organizationId, cancellationToken);
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
