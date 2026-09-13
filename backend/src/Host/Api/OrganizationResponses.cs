// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Organizations;

namespace MailFathom.Host.Api;

/// <summary>What recording an organization carries.</summary>
/// <param name="DisplayName">The name an operator reads it by.</param>
/// <param name="ShortName">The short name its members sign in under, folded to upper case.</param>
internal sealed record OrganizationProvisioningRequest(string? DisplayName, string? ShortName);

/// <summary>What renaming an organization carries.</summary>
/// <param name="DisplayName">The new display name.</param>
internal sealed record OrganizationDisplayNameRequest(string? DisplayName);

/// <summary>What changing an organization's short name carries.</summary>
/// <param name="ShortName">The new short name.</param>
internal sealed record OrganizationShortNameRequest(string? ShortName);

/// <summary>What moving a user between organizations carries.</summary>
/// <param name="OrganizationId">The organization to move the user into, or <see langword="null" /> to leave them in none.</param>
internal sealed record UserOrganizationRequest(Guid? OrganizationId);

/// <summary>One organization as a listing publishes it.</summary>
/// <param name="Id">The identifier every act on it names.</param>
/// <param name="DisplayName">The name an operator reads it by.</param>
/// <param name="ShortName">The short name its members sign in under.</param>
/// <param name="Members">How many users belong to it.</param>
/// <param name="CreatedAt">When it was recorded.</param>
internal sealed record OrganizationResponse(
    Guid Id,
    string DisplayName,
    string ShortName,
    int Members,
    DateTimeOffset CreatedAt)
{
    internal static OrganizationResponse For(Organization organization)
    {
        ArgumentNullException.ThrowIfNull(organization);

        return new OrganizationResponse(
            organization.Id,
            organization.DisplayName,
            organization.ShortName.Value,
            organization.Members,
            organization.CreatedAt);
    }
}

/// <summary>The organizations a deployment holds.</summary>
/// <param name="Organizations">The organizations, ordered by short name.</param>
internal sealed record OrganizationListResponse(IReadOnlyList<OrganizationResponse> Organizations);

/// <summary>What recording an organization answers with.</summary>
/// <param name="OrganizationId">The identifier the organization was minted under.</param>
internal sealed record OrganizationProvisionedResponse(Guid OrganizationId);
