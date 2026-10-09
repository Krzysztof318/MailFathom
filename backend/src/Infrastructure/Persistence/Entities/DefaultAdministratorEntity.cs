// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The record that this deployment wrote its default administrator, and whether it applied that administrator's password setting.</summary>
/// <remarks>
/// <para>
/// One row per deployment, which a check constraint holds to one: the row is what makes recording the administrator
/// happen once, because every replica's first start inserts it and only one insert lands. It outlives the user it names —
/// erasing the user clears <see cref="UserId" /> and leaves the row, which is what keeps a removed administrator removed.
/// </para>
/// <para>
/// Recovering a deployment locked out of every administrative account is the database's on purpose: an operator clears
/// <see cref="PasswordSettingAppliedAt" /> and deletes the administrator's password credential, then starts with the
/// setting carrying a new value. No route can do either, because a route that could would be one more way in.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class DefaultAdministratorEntity
{
    /// <summary>The table the record lives in.</summary>
    internal const string TableName = "default_administrator";

    /// <summary>The one identifier the row may carry.</summary>
    internal const short SingleRowId = 1;

    /// <summary>Gets or sets the row's identifier, which is always <see cref="SingleRowId" />.</summary>
    public short Id { get; set; }

    /// <summary>Gets or sets the default administrator, or <see langword="null" /> once it was erased.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Gets or sets when the default administrator was recorded.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Gets or sets when the password setting was applied, or <see langword="null" /> while it never was.</summary>
    public DateTimeOffset? PasswordSettingAppliedAt { get; set; }
}
