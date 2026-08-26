// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Threads;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the conversation reader the thread use cases and the tools that publish them read through.</summary>
/// <remarks>
/// The four guarantees asserted here are the ones the real reader gets from its query, and the reason this double is a
/// hand-written fake rather than a substitute: a fake that returned everything in whatever order a test wrote it would
/// let a regression dropping the merge walk, the visibility narrowing, the ordering, or the bound pass every suite
/// built on it.
/// </remarks>
public sealed class StubEmailThreadReaderTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly MailAccountId OtherAccount = MailAccountId.Create("home");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("JUNK");

    private static readonly EmailThreadId Thread = EmailThreadId.Create(Guid.CreateVersion7());

    [Fact]
    public async Task ReadEmailsAsync_AConversationHeldUnderTheThreadAsked_AnswersTheMessagesItNames()
    {
        // Arrange
        var opening = Email(1);
        var reply = Email(2);
        var reader = new StubEmailThreadReader((Thread, opening), (Thread, reply));

        // Act
        var read = await ReadAsync(reader, Thread);

        // Assert
        Assert.Equal([opening, reply], read);
        Assert.Equal(1, reader.ReadCount);
    }

    /// <summary>An identifier a tool published before a merge still names the conversation that survived it.</summary>
    [Fact]
    public async Task ReadEmailsAsync_AThreadFoldedIntoAnother_AnswersTheConversationThatSurvivedTheMerge()
    {
        // Arrange
        var folded = EmailThreadId.Create(Guid.CreateVersion7());
        var opening = Email(1);
        var reader = new StubEmailThreadReader((Thread, opening)).MergedInto(folded, Thread);

        // Act
        var read = await ReadAsync(reader, folded);

        // Assert
        Assert.Equal([opening], read);
    }

    /// <summary>A chain of merges is walked to its end, and one that loops fails on the assertion rather than hanging.</summary>
    [Fact]
    public async Task ReadEmailsAsync_AThreadFoldedTwice_WalksTheMergesToTheConversationThatSurvivedThemAll()
    {
        // Arrange
        var first = EmailThreadId.Create(Guid.CreateVersion7());
        var second = EmailThreadId.Create(Guid.CreateVersion7());
        var opening = Email(1);
        var reader = new StubEmailThreadReader((Thread, opening))
            .MergedInto(first, second)
            .MergedInto(second, Thread);

        // Act
        var read = await ReadAsync(reader, first);

        // Assert
        Assert.Equal([opening], read);
    }

    /// <summary>The scope narrows the answer, so a message in a folder nothing admits stays out of the conversation.</summary>
    [Fact]
    public async Task ReadEmailsAsync_AMessageTheScopeDoesNotAdmit_LeavesItOutOfTheConversation()
    {
        // Arrange
        var readable = Email(1);
        var elsewhere = Email(2) with { AccountId = OtherAccount };
        var withheld = Email(3) with { FolderAlias = Junk };
        var reader = new StubEmailThreadReader((Thread, readable), (Thread, elsewhere), (Thread, withheld));

        // Act
        var read = await ReadAsync(reader, Thread);

        // Assert
        Assert.Equal([readable], read);
    }

    /// <summary>The identity orders the answer, so a conversation reads the same whichever order a test wrote it in.</summary>
    [Fact]
    public async Task ReadEmailsAsync_MessagesWrittenOutOfOrder_AnswersThemInTheOrderOfTheirIdentity()
    {
        // Arrange
        var later = Email(3);
        var earlier = Email(1);
        var reader = new StubEmailThreadReader((Thread, later), (Thread, earlier));

        // Act
        var read = await ReadAsync(reader, Thread);

        // Assert
        Assert.Equal([earlier, later], read);
    }

    /// <summary>The bound cuts one row past the maximum, which is what lets a caller tell a full conversation from a cut one.</summary>
    [Fact]
    public async Task ReadEmailsAsync_AConversationLongerThanTheBound_AnswersOneRowPastTheMaximum()
    {
        // Arrange
        var reader = new StubEmailThreadReader(
        [
            .. Enumerable
                .Range(1, IEmailThreadReader.MaximumAssembledEmails + 10)
                .Select(number => (Thread, Email(number))),
        ]);

        // Act
        var read = await ReadAsync(reader, Thread);

        // Assert
        Assert.Equal(IEmailThreadReader.MaximumAssembledEmails + 1, read.Count);
    }

    [Fact]
    public async Task ReadEmailsAsync_AThreadHoldingNothing_AnswersWithNoMessagesAndStillCountsTheRead()
    {
        // Arrange
        var reader = new StubEmailThreadReader();

        // Act
        var read = await ReadAsync(reader, Thread);

        // Assert
        Assert.Empty(read);
        Assert.Equal(1, reader.ReadCount);
    }

    private static ThreadedEmailSummary Email(int number) => new()
    {
        StoredEmailId = StoredEmailId.Create(Guid.Parse($"0199a0c0-0000-7000-8000-{number:D12}")),
        AccountId = Account,
        FolderAlias = Inbox,
    };

    /// <summary>Builds the scope admitting the account's inbox and withholding its junk folder, as configuration would.</summary>
    private static MailboxScope ReadableScope() => new MailboxScopeResolver(
            new ServingCatalog(SyntheticServedAccount.Of(Account)),
            StubMailFolderParticipation.Mapping(new MailFolderIdentity(Account, Inbox)),
            StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Account, Junk)),
            StubMailFolderMappings.ResolvingNothing)
        .ReadableScope([], [], JunkMailInclusion.Excluded);

    private static Task<IReadOnlyList<ThreadedEmailSummary>> ReadAsync(
        StubEmailThreadReader reader,
        EmailThreadId threadId) =>
        reader.ReadEmailsAsync(threadId, ReadableScope(), TestContext.Current.CancellationToken);

    /// <summary>Serves the accounts a test names, because this project carries no substitute package to produce one.</summary>
    private sealed class ServingCatalog(params IReadOnlyList<ServedMailAccount> served) : ICallerMailAccountCatalog
    {
        public bool SynchronizationEnabled => true;

        public IReadOnlyList<ServedMailAccount> OwnedAccounts => served;

        public MailOwnerId Owner => served.Count is 0 ? SyntheticMailOwner.Deployment : served[0].Owner;
    }
}
