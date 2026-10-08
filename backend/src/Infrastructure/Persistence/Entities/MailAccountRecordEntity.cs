// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One mail account, held as a record of its own rather than inside any user's document.</summary>
/// <remarks>
/// The address, its comparison form, and the display name are columns because the deployment compares them across
/// records; everything else about the mailbox is the document, which the configuration layer binds and nothing here
/// queries into.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class MailAccountRecordEntity
{
    internal const string TableName = "settings_mail_accounts";

    internal const int MaximumEmailAddressLength = Users.MailAccountRecord.MaximumEmailAddressLength;

    internal const int MaximumDisplayNameLength = MailAccountDisplayName.MaximumLength;

    public Guid Id { get; set; }

    public string? EmailAddress { get; set; }

    public string? NormalizedEmailAddress { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>The organization the account belongs to, or <see langword="null" /> for an account in none.</summary>
    /// <remarks>It decides who the account may be assigned to — a user of that same organization, or of none — and nothing about what it serves.</remarks>
    public Guid? OrganizationId { get; set; }

    public required string Document { get; set; }

    public long Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
