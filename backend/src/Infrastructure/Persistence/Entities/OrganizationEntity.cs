// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Organizations;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One organization a deployment groups its users into, and whose short name scopes their Basic logins.</summary>
/// <remarks>
/// A user row names at most one of these, and a password credential names the same one its user does. Both reference it by
/// identifier, so changing the short name rewrites nothing beneath it, and both refuse its deletion while they stand.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class OrganizationEntity
{
    /// <summary>The table these rows live in, named here because a composed statement reads it.</summary>
    internal const string TableName = "organizations";

    /// <summary>The longest display name the column holds.</summary>
    public const int MaximumDisplayNameLength = Organization.MaximumDisplayNameLength;

    /// <summary>The longest short name the column holds.</summary>
    public const int MaximumShortNameLength = OrganizationShortName.MaximumLength;

    /// <summary>The identifier the deployment generated, a version 4 value for the reason a user's is.</summary>
    public Guid Id { get; set; }

    /// <summary>The name an operator reads the organization by.</summary>
    public required string DisplayName { get; set; }

    /// <summary>The short name members sign in under, stored upper-cased and unique across the deployment.</summary>
    public required string ShortName { get; set; }

    /// <summary>When the organization was recorded.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
