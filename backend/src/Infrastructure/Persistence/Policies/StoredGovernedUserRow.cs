// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>One user's row as a write that changes its governing policies reads it.</summary>
/// <remarks>
/// Not the entity, for the reason <see cref="StoredSettingsPolicyRow" /> is not: the document is read only where its
/// length is within what this build reads a user's record from, so a row written beside MailFathom is passed over
/// rather than transferred.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "EF Core materializes this row from the bounded read's result set.")]
internal sealed class StoredGovernedUserRow
{
    /// <summary>Gets or sets the user's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the label the user is told apart by.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Gets or sets the organization the user belongs to, or <see langword="null" /> for none.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>Gets or sets the user's own record, or <see langword="null" /> where it is past the bound and was not sent.</summary>
    public string? Document { get; set; }

    /// <summary>Gets or sets whether the row serves the user on the MCP endpoint.</summary>
    public bool McpEndpointEnabled { get; set; }

    /// <summary>Gets or sets whether the row serves the user on the client endpoint.</summary>
    public bool ClientEndpointEnabled { get; set; }
}
