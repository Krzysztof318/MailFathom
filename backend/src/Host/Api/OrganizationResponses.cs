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
/// <param name="OrganizationId">The organization to move the user into.</param>
/// <param name="None"><see langword="true" /> to take the user out of every organization.</param>
/// <remarks>
/// Leaving every organization is stated rather than inferred from an absent identifier, so a body that carries no
/// decision — an empty object, or a misspelled field the serializer binds to nothing — is refused rather than read as a
/// move out, which would change every login the user's passwords are typed as.
/// </remarks>
internal sealed record UserOrganizationRequest(Guid? OrganizationId, bool? None);

/// <summary>What moving a mail account between organizations carries.</summary>
/// <param name="OrganizationId">The organization to move the account into.</param>
/// <param name="None"><see langword="true" /> to take the account out of every organization.</param>
/// <remarks>Leaving every organization is stated for the reason <see cref="UserOrganizationRequest" /> gives: an account in none is assigned to one user in none, so a body read as a move out would decide who may be assigned it.</remarks>
internal sealed record MailAccountOrganizationRequest(Guid? OrganizationId, bool? None);

/// <summary>One organization as a listing publishes it.</summary>
/// <param name="Id">The identifier every act on it names.</param>
/// <param name="DisplayName">The name an operator reads it by.</param>
/// <param name="ShortName">The short name its members sign in under.</param>
/// <param name="Members">How many users belong to it.</param>
/// <param name="MailAccounts">How many mail accounts belong to it.</param>
/// <param name="CreatedAt">When it was recorded.</param>
internal sealed record OrganizationResponse(
    Guid Id,
    string DisplayName,
    string ShortName,
    int Members,
    int MailAccounts,
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
            organization.MailAccounts,
            organization.CreatedAt);
    }
}

/// <summary>One organization row this build will not read as one.</summary>
/// <param name="Id">The identifier every act on it names, which is what an operator repairs the row with.</param>
/// <param name="DisplayName">The name an operator reads it by.</param>
/// <param name="Correction">What the stored short name must become.</param>
/// <remarks>The stored short name itself is not published, for the reason <see cref="UnreadableOrganization" /> gives: it is the one value that failed every rule about what a short name may hold.</remarks>
internal sealed record UnreadableOrganizationResponse(Guid Id, string DisplayName, string Correction)
{
    internal static UnreadableOrganizationResponse For(UnreadableOrganization organization)
    {
        ArgumentNullException.ThrowIfNull(organization);

        return new UnreadableOrganizationResponse(
            organization.Id,
            organization.DisplayName,
            organization.Correction);
    }
}

/// <summary>One page of the organizations a deployment holds.</summary>
/// <param name="Organizations">The organizations, in identifier order.</param>
/// <param name="Unreadable">The rows on this page this build will not read as an organization, which are held back one at a time rather than refusing the listing.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record OrganizationListResponse(
    IReadOnlyList<OrganizationResponse> Organizations,
    IReadOnlyList<UnreadableOrganizationResponse> Unreadable,
    string? NextCursor);

/// <summary>What recording an organization answers with.</summary>
/// <param name="OrganizationId">The identifier the organization was minted under.</param>
internal sealed record OrganizationProvisionedResponse(Guid OrganizationId);
