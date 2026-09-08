// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails.Threads;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails.Threads;

/// <summary>
/// Covers what a page's conversation sizes ask PostgreSQL for, which the C# they are written in does not show. A count
/// taken in this process would answer the same integers while having carried every message of every conversation on the
/// page across the boundary, and a count that dropped the shared narrowing would answer plausible integers that report
/// the size of a folder an operator withheld. Neither failure has a result to read, so the command is what is read.
/// </summary>
public sealed class StoredEmailThreadSizeCommandTests
{
    private static MailboxScope WholeMailbox { get; } = MailboxScope.Create(
        SyntheticMailUser.Deployment,
        [MailAccountId.Create("primary")],
        []);

    /// <summary>
    /// The database groups and counts, which is what makes a page of fifty rows one small answer rather than every
    /// message of every conversation those rows belong to.
    /// </summary>
    [Fact]
    public void Counted_APageOfConversations_AsksPostgreSqlToCountThemRatherThanReturningTheirMessages()
    {
        // Act
        var command = CountCommand();

        // Assert
        Assert.Contains("count(", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", command, StringComparison.Ordinal);
    }

    /// <summary>
    /// The count is narrowed by the same predicate every mail-returning read composes. Without it the number a row
    /// draws would include mail this caller may not see, which is a withheld folder's size published one integer at a
    /// time — and the architecture rule reads the class rather than the query, so nothing else catches its absence.
    /// </summary>
    [Fact]
    public void Counted_APageOfConversations_NarrowsTheCommandByWhatTheScopeAdmits()
    {
        // Act
        var narrowing = NarrowingIn(CountCommand());

        // Assert
        Assert.Contains(nameof(StoredEmailEntity.UserId), narrowing, StringComparison.Ordinal);
        Assert.Contains(nameof(StoredEmailEntity.MailboxAccountId), narrowing, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page is what bounds this read, so the conversations it named narrow the query rather than a walk of every
    /// conversation the scope admits.
    /// </summary>
    [Fact]
    public void Counted_APageOfConversations_NarrowsTheCommandToTheConversationsThePageNamed()
    {
        // Act
        var narrowing = NarrowingIn(CountCommand());

        // Assert
        Assert.Contains($"\"{nameof(StoredEmailEntity.EmailThreadId)}\" = ANY", narrowing, StringComparison.Ordinal);
    }

    /// <summary>Generates the command, without opening a connection.</summary>
    private static string CountCommand()
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return StoredEmailThreadReader
            .Counted(
                context.StoredEmails.AsNoTracking(),
                [new Guid("11111111-1111-1111-1111-111111111111")],
                WholeMailbox)
            .ToQueryString();
    }

    /// <summary>The command from its first narrowing onward, so a column named in the projection is not read as one.</summary>
    private static string NarrowingIn(string command) =>
        command[command.IndexOf("WHERE", StringComparison.Ordinal)..];
}
