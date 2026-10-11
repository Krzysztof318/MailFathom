// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The settings policy one scope holds: the deployment's, or one organization's.</summary>
/// <remarks>
/// <para>
/// One row per scope, and the scope is the organization column: the row naming none is the deployment's, and every
/// other row is the policy of the organization it names. A scope that holds no row states nothing, which is why no
/// row is written for an organization when it is recorded.
/// </para>
/// <para>
/// A row of its own rather than a section of the deployment's persisted configuration document, for the reasons
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0037-a-settings-policy-at-the-deployment-and-at-each-organization.md">ADR 0037</see>
/// gives: a policy is written whole against its own version, by a caller who may hold a grant at one organization
/// alone, and what it forces has to be a row every replica reads rather than a value composed per process.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class SettingsPolicyEntity
{
    /// <summary>The table the policies are held in, beside the documents they govern.</summary>
    internal const string TableName = "settings_policies";

    /// <summary>Gets or sets the row's identifier, minted by the write that first stores the scope's policy.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the organization whose policy this is, or <see langword="null" /> on the deployment's own.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>Gets or sets the policy, as the JSON object the row holds.</summary>
    public required string Document { get; set; }

    /// <summary>Gets or sets the version a writer states and is refused against, which starts at one.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets when the scope's policy was first stored.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the policy last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
