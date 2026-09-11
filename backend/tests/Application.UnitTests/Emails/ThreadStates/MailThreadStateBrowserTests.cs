// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.ThreadStates;

/// <summary>Covers the read a thread screen asks for the block beside a conversation.</summary>
/// <remarks>
/// What the query itself admits is the store's and is asserted where that predicate lives. What is asserted here is the
/// scope this read composes, the permission it is behind, and the absence it answers with rather than failing.
/// </remarks>
public sealed class MailThreadStateBrowserTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly EmailThreadId Conversation = EmailThreadId.Create(Guid.CreateVersion7());

    [Fact]
    public async Task ReadStateAsync_AConversationWithARecordedState_AnswersWithIt()
    {
        // Arrange
        var stored = State([Agreement("The response time stays at two hours.")]);
        var reader = ReaderReturning(stored);
        var browser = CreateBrowser(reader);

        // Act
        var state = await browser.ReadStateAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(stored, state);
    }

    /// <summary>
    /// A state recorded while three statements of an aspect were kept is read as the first of each aspect, which is the
    /// one the derivation ranked most important, rather than derived again to arrive there.
    /// </summary>
    [Fact]
    public async Task ReadStateAsync_AStoredStateHoldingThreeStatementsOfOneAspect_AnswersWithTheFirstOfEachAspect()
    {
        // Arrange
        var question = ThreadStateEntry.Create(
            ThreadStateAspect.OpenQuestion,
            "Whether the yard can be spared on the ninth.",
            [StoredEmailId.Create(Guid.CreateVersion7())]);
        var stored = State(
        [
            Agreement("The response time stays at two hours."),
            Agreement("The fixings are inside the figure."),
            Agreement("Delivery is on the ninth."),
            question,
        ]);
        var browser = CreateBrowser(ReaderReturning(stored));

        // Act
        var state = await browser.ReadStateAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(state);
        Assert.Equal([stored.Entries[0], question], state.Entries);
    }

    /// <summary>
    /// A conversation this deployment has derived nothing about is an absence a screen draws, so the read answers with
    /// nothing rather than raising anything a caller would have to catch.
    /// </summary>
    [Fact]
    public async Task ReadStateAsync_AConversationNothingHasBeenDerivedAbout_AnswersWithNothing()
    {
        // Arrange
        var browser = CreateBrowser(ReaderReturning(null));

        // Act
        var state = await browser.ReadStateAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(state);
    }

    /// <summary>
    /// The conversation and the block beside it are read across the same mail: every account, no folder narrowing, and
    /// junk included, because a reply that landed in junk is part of the exchange somebody is reading.
    /// </summary>
    [Fact]
    public async Task ReadStateAsync_AnyConversation_ReadsItAcrossTheSameMailTheConversationIsReadAcross()
    {
        // Arrange
        var reader = ReaderReturning(null);
        var browser = CreateBrowser(reader);

        // Act
        await browser.ReadStateAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        await reader.Received(1).ReadStateAsync(
            Conversation,
            Arg.Is<MailboxScope>(scope =>
                scope!.AccountIds.Contains(Account)
                && scope.SelectedFolders.Count == 0
                && scope.IncludesJunkMail),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A deployment serving this caller no account holds no conversation of theirs, so nothing is queried.</summary>
    [Fact]
    public async Task ReadStateAsync_ACallerServedNoAccount_AnswersWithNothingAndQueriesNothing()
    {
        // Arrange
        var reader = ReaderReturning(State([Agreement("They settled the price.")]));
        var browser = CreateBrowser(reader, servedAccounts: []);

        // Act
        var state = await browser.ReadStateAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(state);
        await reader.DidNotReceiveWithAnyArgs().ReadStateAsync(
            Arg.Any<EmailThreadId>(),
            Arg.Any<MailboxScope>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The block is mail this caller reads, so the read is behind the permission every mail read is behind.</summary>
    [Fact]
    public async Task ReadStateAsync_ACallerWithoutTheMailReadGrant_IsRefusedBeforeAnythingIsQueried()
    {
        // Arrange
        var reader = ReaderReturning(State([Agreement("They settled the price.")]));
        var browser = CreateBrowser(reader, authorization: AccessAuthorizations.ForCallerGranted());

        // Act and assert
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            browser.ReadStateAsync(Conversation, TestContext.Current.CancellationToken));

        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
        await reader.DidNotReceiveWithAnyArgs().ReadStateAsync(
            Arg.Any<EmailThreadId>(),
            Arg.Any<MailboxScope>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A statement is a sentence a model wrote about somebody's mail, and the name a commitment is owed by is a person
    /// — so both are scanned, and the aspects, the dates and the sources are this deployment's own values that a
    /// redaction would only corrupt.
    /// </summary>
    [Fact]
    public async Task ReadStateAsync_ADeploymentThatScans_ScansTheStatementAndTheOwnerAndLeavesEverythingElse()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var recorded = Commitment($"They send the signed annex, quoting {Marker}.", $"Karolina {Marker}");
        var browser = CreateBrowser(ReaderReturning(State([recorded])), egressGuard: egress.Guard);

        // Act
        var state = await browser.ReadStateAsync(Conversation, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(state);

        var guarded = Assert.Single(state.Entries);

        Assert.DoesNotContain(Marker, guarded.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, guarded.OwedBy!, StringComparison.Ordinal);
        Assert.Equal(recorded.Aspect, guarded.Aspect);
        Assert.Equal(recorded.Sources, guarded.Sources);
        Assert.Equal(recorded.DueAt, guarded.DueAt);
    }

    private static EmailThreadState State(IReadOnlyList<ThreadStateEntry> entries) =>
        new(
            Conversation,
            ThreadStateCoverage.WholeThread,
            entries,
            new ThreadStateRevision(2, new DateTimeOffset(2026, 9, 7, 16, 0, 0, TimeSpan.Zero)),
            new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero),
            IsCurrent: true);

    private static ThreadStateEntry Agreement(string text) =>
        ThreadStateEntry.Create(
            ThreadStateAspect.Agreement,
            text,
            [StoredEmailId.Create(Guid.CreateVersion7())]);

    private static ThreadStateEntry Commitment(string text, string owedBy) =>
        ThreadStateEntry.Create(
            ThreadStateAspect.Commitment,
            text,
            [StoredEmailId.Create(Guid.CreateVersion7())],
            owedBy,
            new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero));

    private static IStoredThreadStateReader ReaderReturning(EmailThreadState? state)
    {
        var reader = Substitute.For<IStoredThreadStateReader>();
        reader
            .ReadStateAsync(Arg.Any<EmailThreadId>(), Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(state);

        return reader;
    }

    private static MailThreadStateBrowser CreateBrowser(
        IStoredThreadStateReader reader,
        IReadOnlyList<MailAccountId>? servedAccounts = null,
        AccessAuthorization? authorization = null,
        SensitiveContentEgressGuard? egressGuard = null)
    {
        var accounts = servedAccounts ?? [Account];
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([.. accounts.Select(static accountId => SyntheticServedAccount.Of(accountId))]);
        catalog.User.Returns(SyntheticMailUser.Deployment);

        var scopeResolver = new MailboxScopeResolver(
            catalog,
            StubMailFolderParticipation.Mapping(
                [.. accounts.Select(accountId => new MailFolderIdentity(accountId, MailFolderAlias.Create("inbox")))]),
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing);

        return new MailThreadStateBrowser(
            reader,
            scopeResolver,
            egressGuard ?? SensitiveContentEgressGuards.Inactive(),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }
}
