// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the conversation-state route puts on the wire, and what it answers where there is none.</summary>
/// <remarks>
/// What a state says and how it is derived is covered where it is decided. What is asserted here is the transport: that
/// an absence is a <c>404</c> a client draws rather than an error, and that a statement's sources reach the wire in the
/// same spelling the citation route accepts back.
/// </remarks>
public sealed class ClientMailThreadStateEndpointTests
{
    private static readonly EmailThreadId Conversation =
        EmailThreadId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    private static readonly StoredEmailId FirstMessage =
        StoredEmailId.Create(new Guid("33333333-3333-3333-3333-333333333333"));

    private static readonly StoredEmailId SecondMessage =
        StoredEmailId.Create(new Guid("44444444-4444-4444-4444-444444444444"));

    private static readonly DateTimeOffset DerivedAt = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly IStoredThreadStateReader stateReader = Substitute.For<IStoredThreadStateReader>();

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailThreadStateRoute_IsThePathAClientComposes() =>
        Assert.Equal("/threads/{threadId:guid}/state", ClientMailThreadStateEndpoint.MailThreadStateRoute);

    [Fact]
    public async Task ReadStateAsync_AConversationWithARecordedState_AnswersWithTheBlock()
    {
        // Arrange
        this.Holding(State(
        [
            ThreadStateEntry.Create(
                ThreadStateAspect.Agreement,
                "The response time stays at two hours.",
                [FirstMessage, SecondMessage]),
            ThreadStateEntry.Create(
                ThreadStateAspect.Commitment,
                "Karolina sends the revised figures.",
                [SecondMessage],
                "Karolina",
                new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)),
        ]));

        // Act
        var result = await this.ReadAsync();

        // Assert
        var block = Assert.IsType<Ok<ClientMailThreadStateResponse>>(result.Result).Value;

        Assert.NotNull(block);
        Assert.Equal(Conversation.Value, block.ThreadId);
        Assert.Equal(nameof(ThreadStateCoverage.WholeThread), block.Coverage);
        Assert.Equal(DerivedAt, block.DerivedAt);
        Assert.Equal(
            [nameof(ThreadStateAspect.Agreement), nameof(ThreadStateAspect.Commitment)],
            block.Entries.Select(static entry => entry.Aspect));
        Assert.Equal("Karolina", block.Entries[1].OwedBy);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), block.Entries[1].DueAt);
    }

    /// <summary>
    /// A source is followed through the same route a Discover answer's sources are, so it reaches the wire in exactly
    /// the spelling that route reads back.
    /// </summary>
    [Fact]
    public async Task ReadStateAsync_AStatementRestingOnMessages_PublishesThemAsCitationTargets()
    {
        // Arrange
        this.Holding(State(
        [
            ThreadStateEntry.Create(
                ThreadStateAspect.Agreement,
                "The response time stays at two hours.",
                [FirstMessage, SecondMessage]),
        ]));

        // Act
        var result = await this.ReadAsync();

        // Assert
        var block = Assert.IsType<Ok<ClientMailThreadStateResponse>>(result.Result).Value;
        var sources = Assert.Single(block!.Entries).Sources;

        Assert.Equal([FirstMessage.Value, SecondMessage.Value], sources.Select(static source => source.Email));
        Assert.All(sources, static source => Assert.Equal(EmailCitationTarget.Kind, source.Kind));
        Assert.All(sources, static source => Assert.Null(source.Fragment));
        Assert.All(sources, static source => Assert.Null(source.AttachmentPosition));

        // The published targets are exactly what the citation route reads back, rather than a second spelling of one.
        Assert.All(sources, static source => Assert.NotNull(ClientCitationEndpoint.TargetOf(source)));
    }

    /// <summary>
    /// A conversation past the bound arrives as a coverage with no statements, so a client says the exchange is too
    /// long instead of drawing a state derived from part of it.
    /// </summary>
    [Fact]
    public async Task ReadStateAsync_AConversationRecordedAsTooLarge_AnswersWithTheCoverageAndNoStatements()
    {
        // Arrange
        this.Holding(new EmailThreadState(
            Conversation,
            ThreadStateCoverage.ThreadTooLarge,
            [],
            new ThreadStateRevision(400, null),
            DerivedAt));

        // Act
        var result = await this.ReadAsync();

        // Assert
        var block = Assert.IsType<Ok<ClientMailThreadStateResponse>>(result.Result).Value;

        Assert.Equal(nameof(ThreadStateCoverage.ThreadTooLarge), block!.Coverage);
        Assert.Empty(block.Entries);
    }

    /// <summary>
    /// Absence is a state a client draws rather than a failure it reports, and it answers alike for a conversation
    /// nothing was derived about and one this user does not hold.
    /// </summary>
    [Fact]
    public async Task ReadStateAsync_AConversationThisDeploymentHasNoStateFor_AnswersWithNotFound()
    {
        // Arrange
        this.Holding(null);

        // Act
        var result = await this.ReadAsync();

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>The empty identifier names no conversation, so nothing is queried for it.</summary>
    [Fact]
    public async Task ReadStateAsync_TheEmptyIdentifier_AnswersWithNotFoundWithoutQuerying()
    {
        // Arrange
        this.Holding(State([]));

        // Act
        var result = await ClientMailThreadStateEndpoint.ReadStateAsync(
            Guid.Empty,
            this.Browser(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        await this.stateReader.DidNotReceiveWithAnyArgs().ReadStateAsync(
            Arg.Any<EmailThreadId>(),
            Arg.Any<MailboxScope>(),
            Arg.Any<CancellationToken>());
    }

    private static EmailThreadState State(IReadOnlyList<ThreadStateEntry> entries) =>
        new(
            Conversation,
            ThreadStateCoverage.WholeThread,
            entries,
            new ThreadStateRevision(2, new DateTimeOffset(2026, 9, 7, 16, 0, 0, TimeSpan.Zero)),
            DerivedAt);

    private void Holding(EmailThreadState? state) =>
        this.stateReader
            .ReadStateAsync(Arg.Any<EmailThreadId>(), Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(state);

    private Task<Results<Ok<ClientMailThreadStateResponse>, NotFound>> ReadAsync() =>
        ClientMailThreadStateEndpoint.ReadStateAsync(
            Conversation.Value,
            this.Browser(),
            TestContext.Current.CancellationToken);

    private MailThreadStateBrowser Browser()
    {
        var accountId = MailAccountId.Create("work");
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(accountId)]);
        catalog.User.Returns(SyntheticMailUser.Deployment);

        return new MailThreadStateBrowser(
            this.stateReader,
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Mapping(
                    [new MailFolderIdentity(accountId, MailFolderAlias.Create("inbox"))]),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            SensitiveContentEgressGuards.Inactive(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }
}
