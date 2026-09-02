// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Threads;

public sealed class EmailThreadAssemblyTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("personal"));

    [Fact]
    public async Task AssembleAsync_ReplyAssembledAfterTheMessageItAnswers_PutsBothInOneConversationAndLinksThem()
    {
        // Arrange
        var store = StoreAt("2026-08-16T10:00:00Z");
        var assembly = new EmailThreadAssembly(store.Store);
        var opening = Identity(1);
        var reply = Identity(2);

        store.Add(Account, opening, "opening@example.test");
        store.Add(Account, reply, "reply@example.test", "opening@example.test");

        // Act
        await AssembleAsync(assembly, store, opening);
        await AssembleAsync(assembly, store, reply);

        // Assert
        Assert.Equal(store.ThreadOf(opening), store.ThreadOf(reply));
        Assert.Equal(opening, store.AnsweredBy(reply));
        Assert.Null(store.AnsweredBy(opening));
    }

    [Fact]
    public async Task AssembleAsync_ReplyAssembledBeforeTheMessageItAnswers_LinksItWhenThatMessageArrives()
    {
        // Arrange
        var store = StoreAt("2026-08-16T10:00:00Z");
        var assembly = new EmailThreadAssembly(store.Store);
        var reply = Identity(1);
        var opening = Identity(2);

        store.Add(Account, reply, "reply@example.test", "opening@example.test");
        store.Add(Account, opening, "opening@example.test");

        // Act
        await AssembleAsync(assembly, store, reply);
        await AssembleAsync(assembly, store, opening);

        // Assert
        Assert.Equal(store.ThreadOf(opening), store.ThreadOf(reply));
        Assert.Equal(opening, store.AnsweredBy(reply));
    }

    [Fact]
    public async Task AssembleAsync_MessageNamingTwoAssembledConversations_MergesThemIntoTheEarlierOne()
    {
        // Arrange
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-16T10:00:00Z", null));
        var store = new FakeEmailThreadStore(clock);
        var assembly = new EmailThreadAssembly(store.Store);
        var first = Identity(1);
        var second = Identity(2);
        var joining = Identity(3);

        store.Add(Account, first, "first@example.test");
        store.Add(Account, second, "second@example.test");
        store.Add(Account, joining, "joining@example.test", "second@example.test", "first@example.test");

        await AssembleAsync(assembly, store, first);
        var earlier = store.ThreadOf(first);

        clock.Advance(TimeSpan.FromMinutes(1));
        await AssembleAsync(assembly, store, second);
        var later = store.ThreadOf(second);

        clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        await AssembleAsync(assembly, store, joining);

        // Assert
        Assert.Equal(earlier, store.ThreadOf(first));
        Assert.Equal(earlier, store.ThreadOf(second));
        Assert.Equal(earlier, store.ThreadOf(joining));
        Assert.Equal(earlier, store.MergedInto(later!.Value));
        Assert.Null(store.MergedInto(earlier!.Value));
    }

    [Fact]
    public async Task AssembleAsync_TwoRepliesToAMessageNobodyStored_PutsBothInOneConversation()
    {
        // Arrange
        var store = StoreAt("2026-08-16T10:00:00Z");
        var assembly = new EmailThreadAssembly(store.Store);
        var first = Identity(1);
        var second = Identity(2);

        store.Add(Account, first, "first@example.test", "absent@example.test");
        store.Add(Account, second, "second@example.test", "absent@example.test");

        // Act
        await AssembleAsync(assembly, store, first);
        await AssembleAsync(assembly, store, second);

        // Assert
        Assert.Equal(store.ThreadOf(first), store.ThreadOf(second));
        Assert.Null(store.AnsweredBy(first));
        Assert.Null(store.AnsweredBy(second));
    }

    [Fact]
    public async Task AssembleAsync_ReferencedAncestorNobodyStored_StillJoinsTheConversationItNames()
    {
        // Arrange
        var store = StoreAt("2026-08-16T10:00:00Z");
        var assembly = new EmailThreadAssembly(store.Store);
        var known = Identity(1);
        var referring = Identity(2);

        store.Add(Account, known, "known@example.test", answeredInternetMessageId: null, "root@example.test");
        store.Add(Account, referring, "referring@example.test", "absent@example.test", "root@example.test");

        // Act
        await AssembleAsync(assembly, store, known);
        await AssembleAsync(assembly, store, referring);

        // Assert
        Assert.Equal(store.ThreadOf(known), store.ThreadOf(referring));
    }

    [Fact]
    public async Task AssembleAsync_RunTwiceOverTheSameMail_ReachesTheSamePlacementAndStartsNoSecondConversation()
    {
        // Arrange
        var store = StoreAt("2026-08-16T10:00:00Z");
        var assembly = new EmailThreadAssembly(store.Store);
        var opening = Identity(1);
        var reply = Identity(2);

        store.Add(Account, opening, "opening@example.test");
        store.Add(Account, reply, "reply@example.test", "opening@example.test");

        await AssembleAsync(assembly, store, opening);
        await AssembleAsync(assembly, store, reply);

        var thread = store.ThreadOf(reply);
        var threadCountAfterTheFirstPass = store.ThreadCount;

        // Act
        await AssembleAsync(assembly, store, opening);
        await AssembleAsync(assembly, store, reply);

        // Assert
        Assert.Equal(thread, store.ThreadOf(reply));
        Assert.Equal(opening, store.AnsweredBy(reply));
        Assert.Equal(threadCountAfterTheFirstPass, store.ThreadCount);
    }

    [Fact]
    public async Task AssembleAsync_TwoRepliesToOneMessage_HangsBothOfThemFromIt()
    {
        // Arrange
        var store = StoreAt("2026-08-16T10:00:00Z");
        var assembly = new EmailThreadAssembly(store.Store);
        var opening = Identity(1);
        var firstReply = Identity(2);
        var secondReply = Identity(3);

        store.Add(Account, opening, "opening@example.test");
        store.Add(Account, firstReply, "first@example.test", "opening@example.test");
        store.Add(Account, secondReply, "second@example.test", "opening@example.test");

        // Act
        await AssembleAsync(assembly, store, opening);
        await AssembleAsync(assembly, store, firstReply);
        await AssembleAsync(assembly, store, secondReply);

        // Assert
        Assert.Equal(opening, store.AnsweredBy(firstReply));
        Assert.Equal(opening, store.AnsweredBy(secondReply));
        Assert.Equal(store.ThreadOf(opening), store.ThreadOf(secondReply));
    }

    [Fact]
    public async Task AssembleAsync_MessagesNamingEachOtherAsTheirAncestor_RefusesTheRelationThatWouldCloseTheCycle()
    {
        // Arrange
        var store = StoreAt("2026-08-16T10:00:00Z");
        var assembly = new EmailThreadAssembly(store.Store);
        var one = Identity(1);
        var other = Identity(2);

        store.Add(Account, one, "one@example.test", "other@example.test");
        store.Add(Account, other, "other@example.test", "one@example.test");

        // Act
        await AssembleAsync(assembly, store, one);
        await AssembleAsync(assembly, store, other);

        // Assert
        Assert.Equal(store.ThreadOf(one), store.ThreadOf(other));
        Assert.False(
            store.AnsweredBy(one) is not null && store.AnsweredBy(other) is not null,
            "The two messages answer each other, which is the loop a published order could never walk out of.");
    }

    [Fact]
    public async Task AssembleAsync_MessageCarryingNoIdentifierAtAll_KeepsOneConversationOfItsOwnAcrossPasses()
    {
        // Arrange
        var store = StoreAt("2026-08-16T10:00:00Z");
        var assembly = new EmailThreadAssembly(store.Store);
        var unidentified = Identity(1);

        store.Add(Account, unidentified, internetMessageId: null);

        // Act
        await AssembleAsync(assembly, store, unidentified);
        var thread = store.ThreadOf(unidentified);
        await AssembleAsync(assembly, store, unidentified);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(thread, store.ThreadOf(unidentified));
        Assert.Equal(1, store.ThreadCount);
    }

    [Fact]
    public async Task AssembleAsync_SameMessageMirroredInTwoFolders_HangsAReplyFromTheLowestIdentity()
    {
        // Arrange
        var store = StoreAt("2026-08-16T10:00:00Z");
        var assembly = new EmailThreadAssembly(store.Store);
        var inbox = Identity(1);
        var archive = Identity(2);
        var reply = Identity(3);

        store.Add(Account, inbox, "opening@example.test");
        store.Add(Account, archive, "opening@example.test");
        store.Add(Account, reply, "reply@example.test", "opening@example.test");

        // Act
        await AssembleAsync(assembly, store, inbox);
        await AssembleAsync(assembly, store, archive);
        await AssembleAsync(assembly, store, reply);

        // Assert
        Assert.Equal(inbox, store.AnsweredBy(reply));
    }

    private static FakeEmailThreadStore StoreAt(string instant) =>
        new(new FakeTimeProvider(DateTimeOffset.Parse(instant, null)));

    private static async Task<EmailThreadId> AssembleAsync(
        EmailThreadAssembly assembly,
        FakeEmailThreadStore store,
        StoredEmailId storedEmailId)
    {
        await using var session = new CommittingSession();

        return await assembly.AssembleAsync(
            session,
            Account,
            store.Read(storedEmailId),
            store.ThreadOf(storedEmailId),
            TestContext.Current.CancellationToken);
    }

    /// <summary>Identities chosen so the lowest one is the earliest arranged, which is what a tie-break test reads.</summary>
    private static StoredEmailId Identity(int ordinal) =>
        StoredEmailId.Create(new Guid($"00000000-0000-0000-0000-{ordinal:D12}"));

    /// <summary>A session that commits, because nothing here is about a conflict a policy has to retry.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
