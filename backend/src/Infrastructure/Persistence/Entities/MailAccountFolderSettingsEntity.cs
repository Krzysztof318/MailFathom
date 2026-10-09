// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Folders;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One folder an account's document maps, held as a row so a query over every account can filter on it.</summary>
/// <remarks>
/// Written from the bound document in the same transaction as the document, and never edited on its own: the document
/// stays the account's settings, and these rows are what a question about every account's folders is answered from.
/// The alias is held in its normalized form, which is the form every row of the mail graph names a folder by.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class MailAccountFolderSettingsEntity
{
    internal const string TableName = "mail_account_folder_settings";

    internal const int MaximumAliasLength = 128;

    public Guid MailAccountId { get; set; }

    public required string Alias { get; set; }

    public MailFolderSpecialUse? SpecialUse { get; set; }

    public bool IsSynchronized { get; set; }

    public bool IsVisibleToTools { get; set; }

    public bool GeneratesEmbeddings { get; set; }

    public bool IsClassifiedForSpam { get; set; }
}
