// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the drafting route accepts, what it refuses, and what a deployment that drafts nothing answers.</summary>
/// <remarks>
/// The drafting itself is covered where it is decided. What is asserted here is the transport: that a value this
/// deployment cannot honour is refused rather than truncated in silence, that a refusal never quotes back what somebody
/// typed, that a client writing its own reply is never told it made a mistake, and that a claim nothing backs reaches a
/// composer marked rather than hidden.
/// </remarks>
public sealed class ClientReplyDraftingEndpointTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly StoredEmailId Answered = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly StoredEmailId Source = StoredEmailId.Create(Guid.CreateVersion7());

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void ReplyDraftingRoute_IsThePathAClientComposes() =>
        Assert.Equal("/replies/drafting", ClientReplyDraftingEndpoint.ReplyDraftingRoute);

    /// <summary>A composer may offer to draft only where something writes one, which is what this answers before anybody presses it.</summary>
    [Fact]
    public void DraftsReplies_ADeploymentThatDraftsAReply_SaysSo()
    {
        // Act
        var result = ClientReplyDraftingEndpoint.DraftsReplies(Drafting(DraftingReturning(NothingDrafted())));

        // Assert
        Assert.True(result.Value?.DraftsReplies);
    }

    /// <summary>No endpoint and an operator who turned it off are one answer, so no configuration is published to a browser.</summary>
    [Fact]
    public void DraftsReplies_ADeploymentThatDraftsNone_SaysSoWithoutSayingWhy()
    {
        // Act
        var result = ClientReplyDraftingEndpoint.DraftsReplies(drafting: null);

        // Assert
        Assert.False(result.Value?.DraftsReplies);
    }

    [Fact]
    public async Task DraftReplyAsync_ARequestWithNoBodyAtAll_IsRefused()
    {
        // Act
        var result = await ClientReplyDraftingEndpoint.DraftReplyAsync(
            request: null,
            Drafting(DraftingReturning(NothingDrafted())),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A request naming no message names nothing to answer, which is a mistake rather than an empty mailbox.</summary>
    [Fact]
    public async Task DraftReplyAsync_ARequestNamingNoMessage_IsRefused()
    {
        // Act
        var result = await this.DraftAsync(new ClientReplyDraftRequest(Guid.Empty, null, null));

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A text past its bound is refused in the words of the bound rather than shortened where nobody sees it.</summary>
    [Fact]
    public async Task DraftReplyAsync_AnInstructionPastItsBound_IsRefusedWithoutQuotingIt()
    {
        // Arrange
        var typed = new string('i', ReplyDraftRequest.MaximumInstructionLength + 1);

        // Act
        var result = await this.DraftAsync(new ClientReplyDraftRequest(Answered.Value, null, typed));

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain(typed, refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftReplyAsync_ASelectionPastItsBound_IsRefusedWithoutQuotingIt()
    {
        // Arrange
        var typed = new string('s', ReplyDraftRequest.MaximumSelectionLength + 1);

        // Act
        var result = await this.DraftAsync(new ClientReplyDraftRequest(Answered.Value, typed, null));

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain(typed, refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
    }

    /// <summary>Somebody writing their own reply has made no mistake, so a deployment that drafts none says so and stops there.</summary>
    [Fact]
    public async Task DraftReplyAsync_ADeploymentThatDraftsNone_AnswersThatNothingWasDrafted()
    {
        // Act
        var result = await ClientReplyDraftingEndpoint.DraftReplyAsync(
            new ClientReplyDraftRequest(Answered.Value, null, null),
            drafting: null,
            TestContext.Current.CancellationToken);

        // Assert
        var drafted = Assert.IsType<Ok<ClientReplyDraftResponse>>(result.Result).Value;

        Assert.False(drafted?.Drafted);
        Assert.Empty(drafted!.Body);
    }

    /// <summary>A message this user does not hold answers exactly as one nobody holds, so nothing says somebody else's exists.</summary>
    [Fact]
    public async Task DraftReplyAsync_AMessageThisUserDoesNotHold_IsNotFound()
    {
        // Act
        var result = await this.DraftAsync(
            new ClientReplyDraftRequest(Answered.Value, null, null),
            Drafting(SourceReader(holdsTheMessage: false), DraftingReturning(NothingDrafted())));

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>What a sender acts on: the reply, what it asserts, and which of those the correspondence does not back.</summary>
    [Fact]
    public async Task DraftReplyAsync_ADraftedReply_PublishesItsBodyItsClaimsAndItsProposedRecipients()
    {
        // Arrange
        var written = ReplyDraft.Written(
            "Tuesday at ten works, at the price agreed.",
            [
                ReplyDraftClaim.Create("They quoted 4 200.", [Source]),
                ReplyDraftClaim.Create("Tuesday at ten is free.", []),
            ],
            [Recipient()]);

        // Act
        var result = await this.DraftAsync(
            new ClientReplyDraftRequest(Answered.Value, null, null),
            DraftingReturning(written));

        // Assert
        var drafted = Assert.IsType<Ok<ClientReplyDraftResponse>>(result.Result).Value;

        Assert.True(drafted?.Drafted);
        Assert.Equal("Tuesday at ten works, at the price agreed.", drafted!.Body);
        Assert.True(drafted.Claims[0].Supported);
        Assert.Equal(Source.Value, Assert.Single(drafted.Claims[0].Sources).Email);
        Assert.False(drafted.Claims[1].Supported);
        Assert.Empty(drafted.Claims[1].Sources);
        Assert.Equal("karolina@example.test", Assert.Single(drafted.ProposedRecipients).Address);
    }

    /// <summary>A provider that failed leaves the composer as it was, which is not a failure a client reports.</summary>
    [Fact]
    public async Task DraftReplyAsync_ADraftingThatProducedNothing_AnswersThatNothingWasDrafted()
    {
        // Act
        var result = await this.DraftAsync(new ClientReplyDraftRequest(Answered.Value, null, null));

        // Assert
        var drafted = Assert.IsType<Ok<ClientReplyDraftResponse>>(result.Result).Value;

        Assert.False(drafted?.Drafted);
    }

    /// <summary>A person pressing a button the allowance is spent on is told so rather than left with a blank composer.</summary>
    [Fact]
    public async Task DraftReplyAsync_ADeploymentThatHasSpentItsAllowance_ReportsTheCeiling()
    {
        // Arrange
        var writer = Substitute.For<IReplyDraftWriter>();
        writer
            .WriteAsync(Arg.Any<ReplyDraftBrief>(), Arg.Any<CancellationToken>())
            .Returns<Task<ReplyDraft>>(_ => throw MailAnsweringBudgetExhaustedException.PeriodSpent());

        // Act
        var result = await this.DraftAsync(
            new ClientReplyDraftRequest(Answered.Value, null, null),
            Drafting(SourceReader(), writer));

        // Assert
        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    private static EmailAddress Recipient()
    {
        EmailAddress.TryCreate("Karolina", "karolina@example.test", out var karolina);

        return karolina;
    }

    private static ReplyDraft NothingDrafted() => ReplyDraft.Nothing;

    private static IReplyDraftWriter DraftingReturning(ReplyDraft? draft)
    {
        var writer = Substitute.For<IReplyDraftWriter>();
        writer
            .WriteAsync(Arg.Any<ReplyDraftBrief>(), Arg.Any<CancellationToken>())
            .Returns(draft ?? ReplyDraft.Nothing);

        return writer;
    }

    /// <summary>The store a drafting reads from, answering with nothing where the draft is meant to be not found.</summary>
    private static IReplyDraftSourceReader SourceReader(bool holdsTheMessage = true)
    {
        EmailAddress.TryCreate("Karolina", "karolina@example.test", out var karolina);

        var reader = Substitute.For<IReplyDraftSourceReader>();
        reader
            .ReadSourcesAsync(
                Arg.Any<StoredEmailId>(),
                Arg.Any<MailboxScope>(),
                Arg.Any<ReplyDraftBounds>(),
                Arg.Any<CancellationToken>())
            .Returns(holdsTheMessage
                ? new ReplyDraftSources(
                    "The racking quotation",
                    [
                        new ReplyDraftMessage(
                            Source,
                            0,
                            "Karolina",
                            new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero),
                            "It is 4 200 zloty."),
                    ],
                    [new ReplyDraftParticipant(0, karolina)],
                    [])
                : null);

        return reader;
    }

    private static MailReplyDrafting Drafting(IReplyDraftWriter writer) => Drafting(SourceReader(), writer);

    private static MailReplyDrafting Drafting(IReplyDraftSourceReader sourceReader, IReplyDraftWriter writer)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(Account)]);
        catalog.User.Returns(SyntheticMailUser.Deployment);

        var scopeResolver = new MailboxScopeResolver(
            catalog,
            StubMailFolderParticipation.Mapping([new MailFolderIdentity(Account, MailFolderAlias.Create("inbox"))]),
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing);

        return new MailReplyDrafting(
            sourceReader,
            writer,
            scopeResolver,
            SensitiveContentEgressGuards.Inactive(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk),
            derivesStyleFromSentMail: true);
    }

    private Task<Results<Ok<ClientReplyDraftResponse>, NotFound, ProblemHttpResult>> DraftAsync(
        ClientReplyDraftRequest request,
        MailReplyDrafting? drafting = null) =>
        ClientReplyDraftingEndpoint.DraftReplyAsync(
            request,
            drafting ?? Drafting(DraftingReturning(NothingDrafted())),
            TestContext.Current.CancellationToken);

    private Task<Results<Ok<ClientReplyDraftResponse>, NotFound, ProblemHttpResult>> DraftAsync(
        ClientReplyDraftRequest request,
        IReplyDraftWriter writer) =>
        this.DraftAsync(request, Drafting(writer));
}
