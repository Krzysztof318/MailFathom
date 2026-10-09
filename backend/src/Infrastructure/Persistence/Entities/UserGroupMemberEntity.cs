// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One user being a member of one group.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class UserGroupMemberEntity
{
    /// <summary>The table these rows live in.</summary>
    internal const string TableName = "user_group_members";

    /// <summary>The group.</summary>
    public Guid GroupId { get; set; }

    /// <summary>The member.</summary>
    public Guid UserId { get; set; }

    /// <summary>When the user joined the group.</summary>
    public DateTimeOffset AddedAt { get; set; }
}
