// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the mutation routes accept, what they refuse before a use case is reached, and what they put on the wire.</summary>
/// <remarks>
/// The decisions behind the routes — whose mail may be changed, which folder a name means, what may still be withdrawn
/// — are covered where they are taken. What is asserted here is the boundary's own half: the bounds a batch is held
/// to, the request identity a repeat is recognized by, and the translation of every answer onto a published name.
/// </remarks>
public sealed class ClientMailMutationsEndpointTests
{
    private static readonly Guid Message = Guid.Parse("0199a0c0-0000-7000-8000-0000000090a0");

    private static readonly MailAccountId ServedAccount = MailAccountId.Create("work");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly IMailboxMutationRecordStore records = Substitute.For<IMailboxMutationRecordStore>();

    /// <summary>The paths a client appends to the address it was configured with, pinned because the client composes them from constants of its own.</summary>
    [Fact]
    public void Routes_AreThePathsAClientComposes()
    {
        Assert.Equal("/mutations", ClientMailMutationsEndpoint.MutationsRoute);
        Assert.Equal("/mutations/flags", ClientMailMutationsEndpoint.FlagMutationsRoute);
        Assert.Equal("/mutations/flags/withdrawals", ClientMailMutationsEndpoint.FlagWithdrawalsRoute);
        Assert.Equal("/mutations/moves", ClientMailMutationsEndpoint.MoveMutationsRoute);
        Assert.Equal("/mutations/moves/withdrawals", ClientMailMutationsEndpoint.MoveWithdrawalsRoute);
    }

    /// <summary>A read naming nothing is a request with no question in it, and answering it with everything would be the unbounded read this surface refuses.</summary>
    [Fact]
    public async Task ReadChangesAsync_NoRecordNamed_IsRefused()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.ReadChangesAsync(
            [],
            this.ProgressReader(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>The caller supplies the identities, so without a ceiling the size of the answer would be the caller's to choose.</summary>
    [Fact]
    public async Task ReadChangesAsync_MoreRecordsThanTheReadBound_IsRefused()
    {
        // Arrange
        var asked = Enumerable
            .Range(0, ClientMailMutationsEndpoint.MaximumChangesPerRead + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();

        // Act
        var result = await ClientMailMutationsEndpoint.ReadChangesAsync(
            asked,
            this.ProgressReader(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>
    /// A read names its records in the request line, which Kestrel refuses over 8192 bytes before any handler sees it,
    /// so the bound the surface publishes has to be one a request carrying it actually survives.
    /// </summary>
    [Fact]
    public void MaximumChangesPerRead_ARequestNamingThatMany_FitsTheRequestLineKestrelAdmits()
    {
        // Arrange
        const int KestrelMaxRequestLineBytes = 8192;
        var line = "GET /api/client" + ClientMailMutationsEndpoint.MutationsRoute + "?"
            + string.Join(
                '&',
                Enumerable
                    .Range(0, ClientMailMutationsEndpoint.MaximumChangesPerRead)
                    .Select(_ => $"record={Guid.CreateVersion7()}"))
            + " HTTP/1.1";

        // Assert
        Assert.True(line.Length < KestrelMaxRequestLineBytes, $"The request line is {line.Length} bytes.");
    }

    /// <summary>Every fact the use case read reaches the wire, because each of them is one a person acts on rather than waits through.</summary>
    [Fact]
    public void ChangesResponse_AChangeInFlight_CarriesEveryFactAClientActsOn()
    {
        // Arrange
        var recordId = MailboxMutationRecordId.Create(Guid.CreateVersion7());
        var progress = new MailboxChangeProgress(
            recordId,
            StoredEmailId.Create(Message),
            MailboxMutation.Relocate,
            MailboxMutationLifecycle.Converging,
            IsOutcomeUnknown: true,
            AttemptCount: 2,
            MailFathomErrorCode.MailboxUnavailable,
            RecordedAt,
            RecordedAt.AddMinutes(1));

        // Act
        var response = ClientMailChangesResponse.For([progress]);

        // Assert
        var change = Assert.Single(response.Changes);

        Assert.Equal(recordId.Value, change.RecordId);
        Assert.Equal(Message, change.StoredEmailId);
        Assert.Equal(MailboxMutation.Relocate.Name, change.Mutation);
        Assert.Equal(MailboxMutationLifecycle.Converging.Name, change.State);
        Assert.True(change.OutcomeUnknown);
        Assert.Equal(2, change.AttemptCount);
        Assert.Equal(MailFathomErrorCode.MailboxUnavailable.Value, change.LastFailure);
        Assert.Equal(RecordedAt, change.RecordedAt);
        Assert.Equal(RecordedAt.AddMinutes(1), change.StateChangedAt);
    }

    /// <summary>A batch naming no message asks for nothing, and a boundary that accepted it would answer an empty request with a success.</summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_ABatchNamingNoMessage_IsRefused()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(RequestId: null, []),
            this.FlagRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>A body that did not parse is refused rather than read as an empty batch, which is the same answer for the same reason.</summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_NoBody_IsRefused()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            request: null,
            this.FlagRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>The bound is what stops one request from becoming an unbounded number of durable writes.</summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_MoreMessagesThanTheBatchBound_IsRefused()
    {
        // Arrange
        var changes = Enumerable
            .Range(0, ClientMailMutationsEndpoint.MaximumChangesPerRequest + 1)
            .Select(_ => new ClientMailFlagChangeRequest(
                Guid.CreateVersion7(),
                new ClientMailFlagStateRequest(Seen: true, Flagged: null),
                Tags: null))
            .ToArray();

        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(RequestId: null, changes),
            this.FlagRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>
    /// The request identity is what decides whether asking again is the same request, so it is held to a length and a
    /// character set here rather than reaching the record it would be stored on.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("has\0a control character")]
    public async Task SubmitFlagChangesAsync_ARequestIdentityThisSurfaceCannotStore_IsRefused(string requestId)
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(
                requestId,
                [new ClientMailFlagChangeRequest(Message, new ClientMailFlagStateRequest(true, null), null)]),
            this.FlagRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>A batch naming no move asks for nothing, exactly as one naming no flag change does.</summary>
    [Fact]
    public async Task SubmitMovesAsync_ABatchNamingNoMessage_IsRefused()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitMovesAsync(
            new ClientMailMovesRequest(RequestId: null, []),
            this.RelocationRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>
    /// The route's own translation of a change the use case wrote down, driven through the use case rather than through
    /// the response builder: what a defect would swap here is which answer reaches the wire, and only a call that
    /// actually records something proves the recorded one does.
    /// </summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_AChangeTheUseCaseRecords_PutsTheRecordOnTheWire()
    {
        // Arrange
        this.RecordEveryRequest();

        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(
                "call-1",
                [new ClientMailFlagChangeRequest(Message, new ClientMailFlagStateRequest(Seen: true, null), null)]),
            this.FlagRecorder(TargetInInbox()),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailFlagChangesResponse>>(result.Result).Value!.Results);

        Assert.Equal(Message, change.StoredEmailId);
        Assert.Equal(ClientMailChangeOutcomes.Recorded, change.Outcome);
        Assert.Null(change.Detail);
        Assert.NotEmpty(change.Changes);
    }

    /// <summary>
    /// A message the use case cannot find reaches the route as an exception and has to leave it as that one message's
    /// result, because a batch carries on past a message that has gone.
    /// </summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_AMessageTheUseCaseCannotFind_ReportsThatMessageAlone()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(
                "call-1",
                [new ClientMailFlagChangeRequest(Message, new ClientMailFlagStateRequest(Seen: true, null), null)]),
            this.FlagRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailFlagChangesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.MessageNotFound, change.Outcome);
        Assert.Empty(change.Changes);
    }

    /// <summary>
    /// A change the boundary cannot express is that message's refusal rather than the batch's, and it carries the
    /// refusal's own sentence — which is written for somebody to read and names no mail content.
    /// </summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_ATagChangeNamingNoDirection_ReportsThatMessageWithTheRefusalsOwnSentence()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(
                "call-1",
                [
                    new ClientMailFlagChangeRequest(
                        Message,
                        null,
                        new ClientMailTagChangeRequest("sideways", ["$label1"])),
                ]),
            this.FlagRecorder(TargetInInbox()),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailFlagChangesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.ChangeNotUsable, change.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(change.Detail));
        Assert.Empty(change.Changes);
    }

    /// <summary>
    /// The move route's own translation, driven through the use case: what it publishes is the answer that use case
    /// returned rather than one the route decided, so a result carried through the whole call proves the wiring the
    /// response builder's own test cannot.
    /// </summary>
    [Fact]
    public async Task SubmitMovesAsync_AMessageTheUseCaseCannotFind_PublishesTheUseCasesOwnAnswer()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitMovesAsync(
            new ClientMailMovesRequest("call-1", [new ClientMailMoveRequest(Message, "Archive")]),
            this.RelocationRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        var move = Assert.Single(Assert.IsType<Ok<ClientMailMovesResponse>>(result.Result).Value!.Results);

        Assert.Equal(Message, move.StoredEmailId);
        Assert.Equal(ClientMailChangeOutcomes.MessageNotFound, move.Outcome);
        Assert.Null(move.DestinationFolder);
        Assert.Null(move.Change);
    }

    /// <summary>A withdrawal naming nothing is a request with nothing to take back.</summary>
    [Fact]
    public async Task WithdrawFlagChangesAsync_NoRecordNamed_IsRefused()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.WithdrawFlagChangesAsync(
            new ClientMailChangeWithdrawalRequest([]),
            this.Withdrawer(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>The moving half is bounded exactly as the flag half is, and by the same published number.</summary>
    [Fact]
    public async Task WithdrawMovesAsync_MoreRecordsThanTheBatchBound_IsRefused()
    {
        // Arrange
        var asked = Enumerable
            .Range(0, ClientMailMutationsEndpoint.MaximumChangesPerRequest + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();

        // Act
        var result = await ClientMailMutationsEndpoint.WithdrawMovesAsync(
            new ClientMailChangeWithdrawalRequest(asked),
            this.Withdrawer(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>Every answer the moving use case can give reaches the wire under a published name, so a client never meets an outcome it has no branch for.</summary>
    [Fact]
    public void MoveResult_EveryDeclaredOutcome_PublishesAName()
    {
        // Act
        var published = Enum
            .GetValues<MailRelocationOutcome>()
            .Select(outcome => ClientMailMoveResultResponse.NotRecorded(Message, outcome).Outcome)
            .ToArray();

        // Assert
        Assert.All(published, outcome => Assert.False(string.IsNullOrWhiteSpace(outcome)));
        Assert.Equal(published.Length, published.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A recorded move carries the folder it was filed into and the record it is carried by, which is what a client polls on.</summary>
    [Fact]
    public void MoveResult_ARecordedMove_CarriesTheFolderAndTheRecord()
    {
        // Arrange
        var recordId = MailboxMutationRecordId.Create(Guid.CreateVersion7());
        var archive = MailFolderAlias.Create("Archive");
        var recorded = AuthoredMailRelocationResult.Recorded(archive, recordId, MailboxMutationLifecycle.Pending);

        // Act
        var response = ClientMailMoveResultResponse.For(Message, recorded);

        // Assert
        Assert.Equal(ClientMailChangeOutcomes.Recorded, response.Outcome);
        Assert.Equal(archive.Value, response.DestinationFolder);
        Assert.Equal(MailboxMutation.Relocate.Name, response.Change?.Mutation);
        Assert.Equal(recordId.Value, response.Change?.RecordId);
        Assert.Equal(MailboxMutationLifecycle.Pending.Name, response.Change?.State);
    }

    /// <summary>A move that was not written down names no folder and no record, so nothing on the response invites a client to poll for one.</summary>
    [Fact]
    public void MoveResult_AMoveThatWasNotRecorded_NamesNeitherAFolderNorARecord()
    {
        // Act
        var response = ClientMailMoveResultResponse.For(
            Message,
            AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.MessageNotFound));

        // Assert
        Assert.Equal(ClientMailChangeOutcomes.MessageNotFound, response.Outcome);
        Assert.Null(response.DestinationFolder);
        Assert.Null(response.Change);
    }

    private static ProblemHttpResult RefusalOf<TValue>(Results<Ok<TValue>, ProblemHttpResult> result) =>
        Assert.IsType<ProblemHttpResult>(result.Result);

    /// <summary>Builds the scope resolution every one of these use cases reaches its caller's mail through.</summary>
    /// <param name="participation">Which of the caller's folders are reachable, defaulting to none, because most tests here are refused before a folder is read.</param>
    private static MailboxScopeResolver ScopeResolver(StubMailFolderParticipation? participation = null)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(ServedAccount)]);

        return new MailboxScopeResolver(
            catalog,
            participation ?? StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing);
    }

    /// <summary>Answers every open with a freshly recorded row, which is what the routes' recorded answers are read from.</summary>
    /// <remarks>
    /// The identity is not deduplicated, because nothing asserted here asks the same thing twice — recognizing a repeat
    /// is the record store's own contract and is covered against a real database.
    /// </remarks>
    private void RecordEveryRequest() => this.records
        .OpenAsync(Arg.Any<IPersistenceSession>(), Arg.Any<MailboxMutationRequest>(), Arg.Any<CancellationToken>())
        .Returns(call => Task.FromResult(new MailboxMutationRecord
        {
            Id = MailboxMutationRecordId.Create(Guid.CreateVersion7()),
            Request = Assert.IsType<MailboxMutationRequest>(call[1]),
            Stage = MailboxMutationStage.Recorded,
            IsAudited = false,
            RequiresSourceRemoval = false,
            Placement = RemoteEmailPlacement.NotReported(),
            AttemptCount = 0,
            RecordedAt = RecordedAt,
            StageChangedAt = RecordedAt,
            LastFailure = null,
            PlacementObservedAt = null,
            SourceRemovalObservedAt = null,
        }));

    /// <summary>The caller's own message, in a folder the scope above reaches.</summary>
    private static AuthoredMailboxTarget TargetInInbox()
    {
        var folder = MailFolderResolution.FirstBindingOf(Inbox, RemoteFolderPath.Create(Inbox.Value));

        return new AuthoredMailboxTarget(
            SyntheticMailOwner.Deployment,
            EmailOccurrenceId.Create(ServedAccount, folder.Id, ImapUidValidity.Create(42), ImapUid.Create(7)),
            folder);
    }

    private static OptimisticConcurrencyRetryPolicy CommitPolicy()
    {
        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new OptimisticConcurrencyRetryPolicy(
            sessions,
            new PersistenceConcurrencyOptions(),
            new FakeTimeProvider(RecordedAt));
    }

    private MailboxChangeProgressReader ProgressReader() => new(
        AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
        ScopeResolver(),
        this.records);

    private MailboxChangeWithdrawer Withdrawer() => new(
        AccessAuthorizations.ForCallerGranted(
            MailFathomPermission.MailFlagsWrite,
            MailFathomPermission.MailMove),
        ScopeResolver(),
        this.records,
        CommitPolicy());

    /// <summary>Builds the flag-change use case the routes are given.</summary>
    /// <param name="target">The message the caller names, defaulting to none, which is the absence the recorder reports as a message that has gone.</param>
    private MailFlagChangeRecorder FlagRecorder(AuthoredMailboxTarget? target = null)
    {
        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        return new MailFlagChangeRecorder(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite),
            ScopeResolver(
                target is null
                    ? null
                    : StubMailFolderParticipation.Mapping(new MailFolderIdentity(ServedAccount, Inbox))),
            targets,
            this.records,
            CommitPolicy());
    }

    private MailRelocationRecorder RelocationRecorder() => new(
        AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailMove),
        ScopeResolver(),
        Substitute.For<IAuthoredMailboxTargetReader>(),
        DestinationResolver(),
        Substitute.For<IAuthoredDeleteEmailDispositionReader>(),
        this.records,
        CommitPolicy());

    /// <summary>Builds a destination resolver that reaches nothing, because no test here gets as far as resolving a folder.</summary>
    private static MailboxDestinationResolver DestinationResolver() => new(
        StubMailFolderMappings.ResolvingNothing,
        Substitute.For<IMailFolderResolutionStore>(),
        new MailFolderResolver(
            Substitute.For<IRemoteFolderCatalog>(),
            Substitute.For<IRemoteFolderCreator>(),
            Substitute.For<IMailFolderResolutionStore>(),
            Substitute.For<IMailFolderMappingChangeAuditor>(),
            Substitute.For<IPersistenceSessionFactory>(),
            ClientSignalPublishers.ReachingNobody,
            new FakeTimeProvider(RecordedAt)),
        Substitute.For<IMailTransportSecurityPolicyReader>());
}
