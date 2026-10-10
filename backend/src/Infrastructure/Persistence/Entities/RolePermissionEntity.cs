// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One entry of one role's list: a published permission name, or a pattern reaching some.</summary>
/// <remarks>
/// A name is stored as written rather than as an ordinal, because it is an identity that outlives a rename of
/// whatever implements it, and a pattern is stored as written rather than as the names it reached, because what it
/// reaches is the reading build's to decide. A stored entry that grants nothing in the running build — a name it does
/// not publish, a pattern reaching nothing — is read back as unpublished and granted to nobody, rather than refusing
/// the role it sits on.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class RolePermissionEntity
{
    /// <summary>The table these rows live in.</summary>
    internal const string TableName = "role_permissions";

    /// <summary>The longest entry the column holds, comfortably above every published name and so above every pattern reaching one.</summary>
    public const int MaximumPermissionLength = 128;

    /// <summary>The role listing the permission.</summary>
    public Guid RoleId { get; set; }

    /// <summary>The entry as it was written: a permission's published name, or a pattern.</summary>
    public required string Permission { get; set; }
}
