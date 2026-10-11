// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>One scope's settings policy as the bounded read answers it.</summary>
/// <remarks>
/// Not the entity, because the read is not of the column: it is of the column's length, and of its text only where
/// that length is within what this build reads a policy from.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "EF Core materializes this row from the bounded read's result set.")]
internal sealed class StoredSettingsPolicyRow
{
    /// <summary>Gets or sets the organization the row belongs to, or <see langword="null" /> for the deployment's own.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>Gets or sets the octets the document occupies as text, whether or not it was sent.</summary>
    public int Length { get; set; }

    /// <summary>Gets or sets the document, or <see langword="null" /> where it is past the bound and was not sent.</summary>
    public string? Document { get; set; }

    /// <summary>Gets or sets the version the row stands at.</summary>
    public long Version { get; set; }
}
