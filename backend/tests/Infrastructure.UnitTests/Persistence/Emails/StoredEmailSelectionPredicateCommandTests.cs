// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>
/// Covers what the shared predicate asks PostgreSQL for, which the C# it is written in does not show. The generated
/// command is read rather than the result, because a filter that translates to the wrong operator returns the right
/// rows and reads none of the index that was built for it.
/// </summary>
public sealed class StoredEmailSelectionPredicateCommandTests
{
    private static MailboxScope WholeMailbox { get; } = MailboxScope.Create(
        SyntheticMailOwner.Deployment,
        [MailAccountId.Create("primary")],
        []);

    /// <summary>
    /// The flag is a column of its own, so the filter is a comparison rather than anything the snapshot has to be
    /// unpacked for — and it is a comparison against the value the caller asked for. Reading only the column name would
    /// pass a predicate that always compared against <see langword="true" />, which is the regression worth catching:
    /// the two rows of this theory would then be one assertion made twice.
    /// </summary>
    [Theory]
    [InlineData(true, "True")]
    [InlineData(false, "False")]
    public void Matching_FlaggedFilter_ComparesTheStoredFlagColumnAgainstTheRequestedValue(
        bool isRemotelyFlagged,
        string boundValue)
    {
        // Act
        var command = CommandFor(SelectionWith(isRemotelyFlagged: isRemotelyFlagged));

        // Assert
        Assert.Contains(
            $"\"{nameof(StoredEmailEntity.IsRemotelyFlagged)}\" = @isRemotelyFlagged",
            NarrowingIn(command),
            StringComparison.Ordinal);
        Assert.Contains($"@isRemotelyFlagged='{boundValue}'", DeclarationsIn(command), StringComparison.Ordinal);
    }

    /// <summary>
    /// The keyword filter has to reach the array's containment operator, which is what the column's GIN index serves.
    /// A translation to <c>= ANY</c> would return the same rows off a sequential scan, so the operator is the assertion.
    /// </summary>
    [Fact]
    public void Matching_KeywordFilter_AsksForContainmentOverTheKeywordArray()
    {
        // Act
        var narrowing = NarrowingOf(SelectionWith(keyword: "$Junk"));

        // Assert
        Assert.Contains(nameof(StoredEmailEntity.RemoteKeywords), narrowing, StringComparison.Ordinal);
        Assert.Contains("@>", narrowing, StringComparison.Ordinal);
    }

    /// <summary>A filter nobody named narrows nothing, which is what keeps an unfiltered listing off both columns.</summary>
    [Fact]
    public void Matching_NeitherFilterNamed_LeavesBothColumnsOutOfTheCommand()
    {
        // Act
        var narrowing = NarrowingOf(SelectionWith());

        // Assert
        Assert.DoesNotContain(nameof(StoredEmailEntity.IsRemotelyFlagged), narrowing, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(StoredEmailEntity.RemoteKeywords), narrowing, StringComparison.Ordinal);
    }

    /// <summary>
    /// A question asked about one conversation reads that conversation in the database rather than in the process. The
    /// column is the assertion: a narrowing applied to the result instead would return the same rows for a small
    /// mailbox and read everything the scope admits to get them.
    /// </summary>
    [Fact]
    public void WithinScope_AScopeNarrowedToOneConversation_NarrowsTheCommandByTheThreadColumn()
    {
        // Act
        var narrowing = ScopeNarrowingOf(WholeMailbox.NarrowedToThread(
            EmailThreadId.Create(new Guid("11111111-1111-1111-1111-111111111111"))));

        // Assert
        Assert.Contains(nameof(StoredEmailEntity.EmailThreadId), narrowing, StringComparison.Ordinal);
    }

    /// <summary>
    /// Individually selected messages narrow by identity, which is what makes a question about four messages read four
    /// rows. The containment operator is what serves that from the primary key rather than from a scan.
    /// </summary>
    [Fact]
    public void WithinScope_AScopeNarrowedToSelectedEmails_NarrowsTheCommandByThoseIdentities()
    {
        // Act
        var narrowing = ScopeNarrowingOf(WholeMailbox.NarrowedToEmails(
        [
            StoredEmailId.Create(new Guid("22222222-2222-2222-2222-222222222222")),
            StoredEmailId.Create(new Guid("33333333-3333-3333-3333-333333333333")),
        ]));

        // Assert
        Assert.Contains($"\"{nameof(StoredEmailEntity.Id)}\" = ANY", narrowing, StringComparison.Ordinal);
    }

    /// <summary>A scope nobody narrowed reads the mailbox, which is what keeps an ordinary listing off both columns.</summary>
    [Fact]
    public void WithinScope_AScopeNarrowedToNeither_LeavesBothNarrowingsOutOfTheCommand()
    {
        // Act
        var narrowing = ScopeNarrowingOf(WholeMailbox);

        // Assert
        Assert.DoesNotContain(nameof(StoredEmailEntity.EmailThreadId), narrowing, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"{nameof(StoredEmailEntity.Id)}\" = ANY", narrowing, StringComparison.Ordinal);
    }

    /// <summary>Generates what a scope alone narrows by, without opening a connection.</summary>
    private static string ScopeNarrowingOf(MailboxScope scope)
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return NarrowingIn(StoredEmailSelectionPredicate
            .WithinScope(context.StoredEmails.AsNoTracking(), scope)
            .ToQueryString());
    }

    /// <summary>Generates what the predicate narrows by, without opening a connection.</summary>
    /// <remarks>
    /// Only the part after <c>WHERE</c> is read. Every flag column is in the select list of any query over this table,
    /// so a test written against the whole command would report a filter as present whether or not one was applied.
    /// </remarks>
    private static string NarrowingOf(MailboxEmailSelection selection) => NarrowingIn(CommandFor(selection));

    /// <summary>Generates the whole command, declarations included, without opening a connection.</summary>
    private static string CommandFor(MailboxEmailSelection selection)
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return StoredEmailSelectionPredicate
            .Matching(context.StoredEmails.AsNoTracking(), selection)
            .ToQueryString();
    }

    private static string NarrowingIn(string command)
    {
        var whereIndex = command.IndexOf("WHERE", StringComparison.Ordinal);

        return whereIndex < 0 ? string.Empty : command[whereIndex..];
    }

    /// <summary>Keeps the parameter declarations EF Core prefixes, which is the one place a bound value is written out.</summary>
    private static string DeclarationsIn(string command)
    {
        var statementStart = command.IndexOf("SELECT", StringComparison.Ordinal);

        return statementStart < 0 ? string.Empty : command[..statementStart];
    }

    private static MailboxEmailSelection SelectionWith(
        bool? isRemotelyFlagged = null,
        string? keyword = null) => MailboxEmailSelection.Create(
        MailboxScope.NothingReadable,
        senderAddress: null,
        recipientAddress: null,
        subjectFragment: null,
        receivedOnOrAfter: null,
        receivedBefore: null,
        isRemotelySeen: null,
        isRemotelyFlagged,
        keyword,
        hasAttachments: null);
}
