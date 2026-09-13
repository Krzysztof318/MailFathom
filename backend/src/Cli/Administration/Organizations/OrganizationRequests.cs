// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Organizations;

/// <summary>The organization an administrator asks a deployment to record.</summary>
/// <param name="DisplayName">The name an operator reads it by.</param>
/// <param name="ShortName">The short name its members sign in under, which the deployment folds to upper case.</param>
/// <remarks>The identifier is not here: the deployment mints one, so a command supplying one would decide an identity it does not own.</remarks>
internal sealed record OrganizationProvisioningRequest(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("shortName")] string ShortName);

/// <summary>The organization a provisioning recorded.</summary>
/// <param name="OrganizationId">The identifier the deployment minted.</param>
internal sealed record OrganizationProvisioned(
    [property: JsonPropertyName("organizationId")] Guid OrganizationId);

/// <summary>The name an organization is read by from now on.</summary>
/// <param name="DisplayName">The new display name.</param>
internal sealed record OrganizationDisplayNameRequest(
    [property: JsonPropertyName("displayName")] string DisplayName);

/// <summary>The short name an organization's members sign in under from now on.</summary>
/// <param name="ShortName">The new short name.</param>
/// <remarks>Its own type rather than a field beside the display name, because the deployment reads the two on different routes and only this one changes how anybody signs in.</remarks>
internal sealed record OrganizationShortNameRequest(
    [property: JsonPropertyName("shortName")] string ShortName);

/// <summary>The organization one user belongs to from now on.</summary>
/// <param name="OrganizationId">The organization to move the user into, or <see langword="null" /> when <paramref name="None" /> says they belong to none.</param>
/// <param name="None">Whether the user is taken out of every organization.</param>
/// <remarks>Leaving every organization is sent as a stated decision rather than as a missing identifier, because the deployment refuses a body that states neither.</remarks>
internal sealed record UserOrganizationRequest(
    [property: JsonPropertyName("organizationId")] Guid? OrganizationId,
    [property: JsonPropertyName("none")] bool None);
