// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Grants;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One role: a name unique across the deployment, whose permissions are the rows of <see cref="RolePermissionEntity" /> naming it.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class RoleEntity
{
    /// <summary>The table these rows live in, named here because a composed statement locks one.</summary>
    internal const string TableName = "roles";

    /// <summary>The longest name the column holds.</summary>
    public const int MaximumNameLength = Role.MaximumNameLength;

    /// <summary>The identifier the deployment generated, a version 7 value like every other it mints.</summary>
    public Guid Id { get; set; }

    /// <summary>The name an operator reads the role by.</summary>
    public required string Name { get; set; }

    /// <summary>When the role was recorded.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
