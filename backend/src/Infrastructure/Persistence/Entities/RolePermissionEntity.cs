// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One permission one role lists, by its published name.</summary>
/// <remarks>
/// The name is stored as written rather than as an ordinal, because it is an identity that outlives a rename of
/// whatever implements it. A stored name the running build does not publish is read back as unpublished and granted to
/// nobody, rather than refusing the role it sits on.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class RolePermissionEntity
{
    /// <summary>The table these rows live in.</summary>
    internal const string TableName = "role_permissions";

    /// <summary>The longest permission name the column holds, comfortably above every published one.</summary>
    public const int MaximumPermissionLength = 128;

    /// <summary>The role listing the permission.</summary>
    public Guid RoleId { get; set; }

    /// <summary>The published name of the permission.</summary>
    public required string Permission { get; set; }
}
