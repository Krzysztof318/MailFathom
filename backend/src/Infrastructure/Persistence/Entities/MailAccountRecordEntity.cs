// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One mail account, held as a record of its own rather than inside any user's document.</summary>
/// <remarks>
/// <para>
/// The address, its comparison form, and the display name are columns because the deployment compares them across
/// records; everything else about the mailbox is the document, which the configuration layer binds.
/// </para>
/// <para>
/// The settings a question about every account at once filters on are columns too, beside the document they are read
/// out of: whether the document binds at all, how the account is synchronized, whether it classifies spam, and what it
/// asks to be scanned for. Its folders are rows of <see cref="MailAccountFolderSettingsEntity" />. Every write of the
/// document by this build writes them in the same transaction, and records in <see cref="SettingsVersion" /> the version
/// of the document they were read out of.
/// </para>
/// <para>
/// A build older than these columns rewrites the document alone during a rolling upgrade, so a row whose
/// <see cref="SettingsVersion" /> differs from <see cref="Version" /> holds settings read out of an earlier document, or
/// none at all, and the migration that added the columns leaves every row it filled the same way. Such rows are read
/// again out of their documents a hundred per convergence interval, so one is read within an interval of its write
/// while no more than that trail, and a larger backlog drains at that rate. Until then every question about every
/// account answers from what the row held before.
/// </para>
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

    public bool HasReadableSettings { get; set; }

    public MailSynchronizationMode SynchronizationMode { get; set; }

    public bool ClassifiesSpam { get; set; }

    public int[] ScansFor { get; set; } = [];

    public int[] ScreensOutgoingMailFor { get; set; } = [];

    public long Version { get; set; }

    /// <summary>The <see cref="Version" /> of the document the settings columns and folder rows were read out of.</summary>
    public long SettingsVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
