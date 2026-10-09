// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Grants;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One group of users a role can be assigned to, in no organization or in exactly one.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class UserGroupEntity
{
    /// <summary>The table these rows live in, named here because a composed statement locks one.</summary>
    internal const string TableName = "user_groups";

    /// <summary>The longest name the column holds.</summary>
    public const int MaximumNameLength = UserGroup.MaximumNameLength;

    /// <summary>The identifier the deployment generated, a version 7 value like every other it mints.</summary>
    public Guid Id { get; set; }

    /// <summary>The name an operator reads the group by.</summary>
    public required string Name { get; set; }

    /// <summary>The organization the group belongs to, or <see langword="null" /> for one in none.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>When the group was recorded.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
