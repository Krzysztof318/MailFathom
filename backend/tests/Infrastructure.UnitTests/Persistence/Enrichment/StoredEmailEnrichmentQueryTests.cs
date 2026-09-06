// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Enrichment;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Enrichment;

/// <summary>
/// Proves the selection is a query PostgreSQL runs rather than one the provider evaluates in the process.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StoredEmailEnrichmentSelectionTests" /> establishes which rows the predicate selects, over objects. What
/// it cannot establish is that the predicate translates: a clause the provider cannot express either throws when the
/// account run first reaches it or, worse, is evaluated after the rows have been read, which turns one account's
/// bounded batch into a read of every message this deployment holds. Neither failure is reachable from a suite with no
/// database — but the SQL the provider would send is, and printing it needs no connection.
/// </para>
/// <para>
/// The shape is worth the assertion because the selection is not a filtered projection: it carries two negated
/// existence conditions over a collection navigation, the batch is bounded by a limit taken after an ordering, and each
/// selected message brings a bounded, separately ordered set of its passages back with it.
/// </para>
/// </remarks>
public sealed class StoredEmailEnrichmentQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Every clause reaches the server, including the two the pipeline's ordering is expressed as.</summary>
    [Fact]
    public void Selecting_TheBatchTheAccountRunReads_TranslatesToOneBoundedStatement()
    {
        // Arrange
        var options = MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null);
        using var context = new MailFathomDbContext(options, PostgresTextSearchConfiguration.Default);

        // Act
        var sql = StoredEmailEnrichmentStore.Selecting(
                context.StoredEmails.AsNoTracking(),
                Guid.CreateVersion7(),
                "work",
                [new MailFolderIdentity(MailAccountId.Create("work"), MailFolderAlias.Create("INBOX"))],
                readsAttachments: true,
                new DerivedWorkAdmissionTerms([], [], [], Now))
            .OrderBy(email => email.Id)
            .Take(8)
            .Select(email => new
            {
                email.Id,
                Passages = email.Chunks
                    .OrderBy(chunk => chunk.Ordinal)
                    .Take(6)
                    .Select(chunk => chunk.Text)
                    .ToList(),
            })
            .ToQueryString();

        // Assert
        Assert.Contains("EXISTS (", sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS (", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
    }
}
