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
    private static MailboxScopeResolver ScopeResolver()
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(MailAccountId.Create("work"))]);

        return new MailboxScopeResolver(
            catalog,
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing);
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

    private MailFlagChangeRecorder FlagRecorder() => new(
        AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite),
        ScopeResolver(),
        Substitute.For<IAuthoredMailboxTargetReader>(),
        this.records,
        CommitPolicy());

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
            new FakeTimeProvider(RecordedAt)),
        Substitute.For<IMailTransportSecurityPolicyReader>());
}
