// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
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
    /// <summary>The flag is a column of its own, so the filter is a comparison rather than anything the snapshot has to be unpacked for.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Matching_FlaggedFilter_ComparesTheStoredFlagColumn(bool isRemotelyFlagged)
    {
        // Act
        var narrowing = NarrowingOf(SelectionWith(isRemotelyFlagged: isRemotelyFlagged));

        // Assert
        Assert.Contains(nameof(StoredEmailEntity.IsRemotelyFlagged), narrowing, StringComparison.Ordinal);
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

    /// <summary>Generates what the predicate narrows by, without opening a connection.</summary>
    /// <remarks>
    /// Only the part after <c>WHERE</c> is read. Every flag column is in the select list of any query over this table,
    /// so a test written against the whole command would report a filter as present whether or not one was applied.
    /// </remarks>
    private static string NarrowingOf(MailboxEmailSelection selection)
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        var command = StoredEmailSelectionPredicate
            .Matching(context.StoredEmails.AsNoTracking(), selection)
            .ToQueryString();

        var whereIndex = command.IndexOf("WHERE", StringComparison.Ordinal);

        return whereIndex < 0 ? string.Empty : command[whereIndex..];
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
