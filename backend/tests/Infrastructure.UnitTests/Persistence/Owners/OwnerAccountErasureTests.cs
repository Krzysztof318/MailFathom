// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Owners;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Owners;

/// <summary>States, by name, which tables erasing an owner has to take itself.</summary>
/// <remarks>
/// <para>
/// The walk is derived from the model so it cannot fall behind the schema, and this is what keeps its answer readable:
/// a table entering or leaving the list is a diff a reviewer sees rather than a silent change in how much of an
/// owner's record an erasure request actually discharges. So a failure here is answered by deciding what the moved
/// table is — one that records a mail account with nothing keying it onto one, or one a cascade already reaches —
/// never by copying the reported names over the expected ones.
/// </para>
/// <para>
/// Two absences are the point of the walk rather than gaps in it. <c>outgoing_email_filings</c> records an account and
/// is not here, because it cascades from <c>outgoing_emails</c>, which is; <c>mailbox_mutations</c> records one too and
/// cascades from the mail it is about. Both are discharged by a statement already in the list, so naming them would be
/// asking the database twice for rows it has already taken.
/// </para>
/// </remarks>
public sealed class OwnerAccountErasureTests
{
    [Fact]
    public void TablesTheCascadeDoesNotReach_TheSchemaAsItStands_NamesEveryTableRecordingAnAccountWithNoKeyOntoOne()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        string[] tables =
        [
            .. OwnerAccountErasure.TablesTheCascadeDoesNotReach(context.Model)
                .Select(entityType => entityType.GetTableName()!),
        ];

        // Assert
        Assert.Equal<string[]>(
            [
                "mail_answering_audit_entries",
                "mail_drafts",
                "mail_rederivation_positions",
                "mail_rederivation_runs",
                "mail_rule_evaluation_runs",
                "mailbox_mutation_audit_entries",
                "mailbox_refresh_tokens",
                "outgoing_emails",
                "recurring_sends",
                "spam_classification_runs",
            ],
            tables);
    }
}
