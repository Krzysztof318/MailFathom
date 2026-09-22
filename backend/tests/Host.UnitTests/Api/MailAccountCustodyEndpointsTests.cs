// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Coordination;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Drain;
using MailFathom.Application.Synchronization.Restore;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the three routes an account's custody is read from, changed on, and has its appends settled on.</summary>
/// <remarks>
/// What is asserted here is what a caller is answered rather than what the switch decided, which
/// <c>MailAccountCustodySwitchTests</c> holds. The status separating the two kinds of negative answer is the whole of
/// this: an account this deployment does not serve is a request to correct, while a refusal names something about the
/// deployment that an operator changes and asks again — so the second is an answer rather than an error.
/// </remarks>
public sealed class MailAccountCustodyEndpointsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes both paths from
    /// constants of its own, and a rename on either side compiles cleanly while the command reaches a 404 that reads
    /// exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void CustodyRoutes_AreThePathsTheCommandComposes()
    {
        Assert.Equal("/accounts/custody", MailAccountCustodyEndpoints.CustodyRoute);
        Assert.Equal("/accounts/custody/switch", MailAccountCustodyEndpoints.CustodySwitchRoute);
    }

    [Fact]
    public async Task ReadAsync_AnAccountThisDeploymentServes_AnswersItsCustodyBesideWhatTheSourceStillHolds()
    {
        // Arrange
        var custody = SwitchOver(CustodyStoreHolding(
            new MailAccountCustodyState(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Mirrored)));
        var drain = DrainOver(new MailboxDrainStanding(
            AwaitingDrain: 4812,
            HeldBackAboveSizeLimit: 3,
            HeldBackAwaitingHeadroom: 0,
            AwaitingSourceRemoval: 7));

        // Act
        var answer = await MailAccountCustodyEndpoints.ReadAsync(
            Account.Value,
            CatalogServing(Account),
            custody,
            drain,
            RestoreOver(),
            TestContext.Current.CancellationToken);

        // Assert
        var state = Assert.IsType<Ok<MailAccountCustodyResponse>>(answer.Result).Value!;
        Assert.Equal(Account.Value, state.Account);
        Assert.Equal("HoldMailbox", state.Requested);
        Assert.Equal("Mirrored", state.Phase);
        Assert.True(state.IsSwitchPending);
        Assert.Equal(4812, state.Drain.AwaitingDrain);
        Assert.Equal(3, state.Drain.HeldBackAboveSizeLimit);
        Assert.Equal(7, state.Drain.AwaitingSourceRemoval);
    }

    /// <summary>
    /// The counts an operator acts on, and the records they act with: a count of unanswered appends with no record
    /// named beside it is a number nobody can settle, which is the shape this route exists to avoid.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ARestoringAccount_AnswersWhatItOwesItsSourceAndNamesEachUnansweredAppend()
    {
        // Arrange
        var custody = SwitchOver(CustodyStoreHolding(
            new MailAccountCustodyState(MailAccountCustody.MirrorSource, MailAccountCustodyPhase.Restoring)));
        var unanswered = new MailboxRestoreAppend(
            MailboxRestoreAppendId.New(),
            StoredEmailId.Create(Guid.CreateVersion7()),
            MailFolderAlias.Create("archive"),
            MailFolderResolutionGeneration.First,
            Moment);

        // Act
        var answer = await MailAccountCustodyEndpoints.ReadAsync(
            Account.Value,
            CatalogServing(Account),
            custody,
            DrainOver(),
            RestoreOver(
                new MailboxRestoreStanding(
                    AwaitingAppend: 318,
                    AwaitingStateWrite: 12,
                    UnansweredAppends: 1,
                    AwaitingConfirmation: 2),
                unanswered),
            TestContext.Current.CancellationToken);

        // Assert
        var restore = Assert.IsType<Ok<MailAccountCustodyResponse>>(answer.Result).Value!.Restore;
        Assert.NotNull(restore);
        Assert.Equal(318, restore.AwaitingAppend);
        Assert.Equal(12, restore.AwaitingStateWrite);
        Assert.Equal(1, restore.UnansweredAppends);
        Assert.Equal(2, restore.AwaitingConfirmation);

        var named = Assert.Single(restore.Unanswered);
        Assert.Equal(unanswered.Id.Value, named.Record);
        Assert.Equal("ARCHIVE", named.Folder);
        Assert.Equal(Moment, named.IssuedAt);
    }

    /// <summary>
    /// The standing query counts a whole mailbox, and an account that is not restoring owes none of it — so the block
    /// is absent rather than zero, and the query never runs for the accounts a deployment mostly holds.
    /// </summary>
    [Theory]
    [InlineData(MailAccountCustodyPhase.Mirrored)]
    [InlineData(MailAccountCustodyPhase.Held)]
    public async Task ReadAsync_AnAccountThatIsNotRestoring_AnswersNoRestoreBlockAndAsksTheStoreNothing(
        MailAccountCustodyPhase phase)
    {
        // Arrange
        var custody = SwitchOver(CustodyStoreHolding(
            new MailAccountCustodyState(MailAccountCustody.HoldMailbox, phase)));
        var store = Substitute.For<IMailboxRestoreStore>();

        // Act
        var answer = await MailAccountCustodyEndpoints.ReadAsync(
            Account.Value,
            CatalogServing(Account),
            custody,
            DrainOver(),
            RestoreOver(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.IsType<Ok<MailAccountCustodyResponse>>(answer.Result).Value!.Restore);
        await store.DidNotReceiveWithAnyArgs().ReadStandingAsync(
            Arg.Any<MailAccountId>(),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReadAsync_AnAccountThisDeploymentDoesNotServe_IsRefusedAsARequestToCorrect()
    {
        // Act
        var answer = await MailAccountCustodyEndpoints.ReadAsync(
            "absent",
            CatalogServing(Account),
            SwitchOver(CustodyStoreHolding(MailAccountCustodyState.Mirrored)),
            DrainOver(),
            RestoreOver(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<ProblemHttpResult>(answer.Result).StatusCode);
    }

    /// <summary>
    /// An account the deployment serves whose record no synchronization run has written yet is a wait rather than a
    /// name to correct, so it is told apart from one this deployment does not serve.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AnAccountServedButNotBoundYet_SaysSoRatherThanCallingItUnknown()
    {
        // Act
        var answer = await MailAccountCustodyEndpoints.ReadAsync(
            Account.Value,
            CatalogServing(Account),
            SwitchOver(CustodyStoreHoldingNothing()),
            DrainOver(),
            RestoreOver(),
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(answer.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("bound no folder yet", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>The switch answers the same way, because there is no record to change either.</summary>
    [Fact]
    public async Task SwitchAsync_AnAccountServedButNotBoundYet_SaysSoRatherThanCallingItUnknown()
    {
        // Act
        var answer = await MailAccountCustodyEndpoints.SwitchAsync(
            new MailAccountCustodySwitchRequest(Account.Value, "HoldMailbox"),
            CatalogServing(Account),
            SwitchOver(CustodyStoreHoldingNothing()),
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(answer.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("bound no folder yet", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SwitchAsync_ARequestNamingNoCustody_IsRefusedNamingTheValuesItCouldHaveNamed()
    {
        // Act
        var answer = await MailAccountCustodyEndpoints.SwitchAsync(
            new MailAccountCustodySwitchRequest(Account.Value, "EmptyEverything"),
            CatalogServing(Account),
            SwitchOver(CustodyStoreHolding(MailAccountCustodyState.Mirrored)),
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(answer.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Contains("HoldMailbox", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SwitchAsync_ARequestWithNoBodyAtAll_IsRefusedRatherThanReadAsAnAccount()
    {
        // Act
        var answer = await MailAccountCustodyEndpoints.SwitchAsync(
            request: null,
            CatalogServing(Account),
            SwitchOver(CustodyStoreHolding(MailAccountCustodyState.Mirrored)),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<ProblemHttpResult>(answer.Result).StatusCode);
    }

    [Fact]
    public async Task SwitchAsync_ACustodyNothingRefuses_AnswersWithWhatTheAccountIsNowAskedToHave()
    {
        // Arrange
        var store = CustodyStoreHolding(MailAccountCustodyState.Mirrored);

        // Act
        var answer = await MailAccountCustodyEndpoints.SwitchAsync(
            new MailAccountCustodySwitchRequest(Account.Value, "holdmailbox"),
            CatalogServing(Account),
            SwitchOver(store),
            TestContext.Current.CancellationToken);

        // Assert
        var outcome = Assert.IsType<Ok<MailAccountCustodySwitchResponse>>(answer.Result).Value!;
        Assert.True(outcome.WasAccepted);
        Assert.Equal("HoldMailbox", outcome.Requested);
        Assert.Empty(outcome.Refusals);
    }

    /// <summary>
    /// A refusal is a true statement about the deployment rather than a fault in the request, so it is answered rather
    /// than raised: the operator reads what to correct and asks again with the same command.
    /// </summary>
    [Fact]
    public async Task SwitchAsync_ARefusedSwitch_IsAnsweredWithTheSentencesRatherThanAnErrorStatus()
    {
        // Arrange
        var leases = Substitute.For<IWorkLeaseStore>();
        leases.ReadEveryHeldAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkLease>>([
                new WorkLease(
                    WorkScope.Create("mail-account:work"),
                    WorkLeaseHolder.NewHold(),
                    ReplicaIdentity.Create("replica-2"),
                    Moment.AddMinutes(1)),
            ]));

        // Act
        var answer = await MailAccountCustodyEndpoints.SwitchAsync(
            new MailAccountCustodySwitchRequest(Account.Value, "HoldMailbox"),
            CatalogServing(Account),
            SwitchOver(CustodyStoreHolding(MailAccountCustodyState.Mirrored), leases),
            TestContext.Current.CancellationToken);

        // Assert
        var outcome = Assert.IsType<Ok<MailAccountCustodySwitchResponse>>(answer.Result).Value!;
        Assert.False(outcome.WasAccepted);
        Assert.Equal("MirrorSource", outcome.Requested);
        Assert.Contains("replica-2", Assert.Single(outcome.Refusals), StringComparison.Ordinal);
    }

    /// <summary>Answers every read of the one account as the state a test stated, and accepts whatever is written.</summary>
    /// <summary>A store with no row for the account, which is every account before its first folder binds.</summary>
    private static IMailAccountCustodyStore CustodyStoreHoldingNothing()
    {
        var store = Substitute.For<IMailAccountCustodyStore>();
        store.ReadAsync(Account, Arg.Any<CancellationToken>()).Returns(Task.FromResult<MailAccountCustodyState?>(null));

        return store;
    }

    private static IMailAccountCustodyStore CustodyStoreHolding(MailAccountCustodyState state)
    {
        var store = Substitute.For<IMailAccountCustodyStore>();
        store.ReadAsync(Account, Arg.Any<CancellationToken>()).Returns(Task.FromResult<MailAccountCustodyState?>(state));
        store.RequestAsync(
                Arg.Any<IPersistenceSession>(),
                Account,
                Arg.Any<MailAccountCustody>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MailAccountCustodyState?>(state));

        return store;
    }

    /// <summary>Composes the use case the routes reach, over ports this suite states and with the grant granted.</summary>
    private static MailAccountCustodySwitch SwitchOver(
        IMailAccountCustodyStore store,
        IWorkLeaseStore? leases = null)
    {
        var mappings = Substitute.For<IMailFolderMappingReader>();
        mappings.FoldersOf(Arg.Any<MailAccountId>()).Returns([]);

        var heldLeases = leases ?? Substitute.For<IWorkLeaseStore>();

        if (leases is null)
        {
            heldLeases.ReadEveryHeldAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WorkLease>>([]));
        }

        return new MailAccountCustodySwitch(
            store,
            mappings,
            Substitute.For<IRemoteFolderCatalog>(),
            Substitute.For<IMailTransportSecurityPolicyReader>(),
            heldLeases,
            Substitute.For<IMailAccountCustodyAuditor>(),
            Substitute.For<IAuthoredDeleteEmailDispositionReader>(),
            CommitPolicy(),
            AccessAuthorizations.ForAdministratorGranted(
                MailFathomPermission.AdminRead,
                MailFathomPermission.AdminCustodyWrite),
            new FakeTimeProvider(Moment));
    }

    /// <summary>Composes the pass the read route asks for its standing figures, over a store this suite states.</summary>
    private static MailboxDrainPass DrainOver(MailboxDrainStanding? standing = null)
    {
        var store = Substitute.For<IMailboxDrainStore>();
        store.ReadStandingAsync(Arg.Any<MailAccountId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(standing ?? new MailboxDrainStanding(0, 0, 0, 0)));

        return new MailboxDrainPass(
            Substitute.For<IMailAccountCustodyStore>(),
            store,
            Substitute.For<IMailboxMutationRecordStore>(),
            Substitute.For<IEmailContentStore>(),
            Substitute.For<IEmailContentRepairRequestStore>(),
            Substitute.For<IMailboxWriteSessionFactory>(),
            Substitute.For<IMailFolderResolutionStore>(),
            Substitute.For<IMailTransportSecurityPolicyReader>(),
            Substitute.For<IMailFolderMappingReader>(),
            CommitPolicy(),
            new MailboxSynchronizationOptions(),
            new FakeTimeProvider(Moment));
    }

    /// <summary>
    /// The settlement acts on an account rather than on a record identity alone, so a name this deployment does not
    /// serve has to be refused before anything is written — the record identity would otherwise be the whole of what
    /// decides which deployment's mailbox an operator's verdict reaches.
    /// </summary>
    [Fact]
    public async Task SettleRestoreAppendAsync_AccountThisDeploymentDoesNotServe_RefusesAndSettlesNothing()
    {
        // Arrange
        var store = Substitute.For<IMailboxRestoreStore>();

        // Act
        var answer = await MailAccountCustodyEndpoints.SettleRestoreAppendAsync(
            new MailAccountRestoreSettlementRequest("elsewhere", Guid.CreateVersion7(), SourceHoldsTheCopy: true),
            CatalogServing(Account),
            SettlementOver(store),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        await store.DidNotReceiveWithAnyArgs()
            .SettleAppendAsync(default!, default, default, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The record is what the verdict is about, and an empty or absent one would reach the store as a value that
    /// matches nothing and be reported back as a record somebody had already settled.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettleRestoreAppendAsync_RequestNamesNoRecord_RefusesAndSettlesNothing(bool empty)
    {
        // Arrange
        var store = Substitute.For<IMailboxRestoreStore>();
        var request = empty
            ? new MailAccountRestoreSettlementRequest(Account.Value, Guid.Empty, SourceHoldsTheCopy: false)
            : null;

        // Act
        var answer = await MailAccountCustodyEndpoints.SettleRestoreAppendAsync(
            request,
            CatalogServing(Account),
            SettlementOver(store),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(answer.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        await store.DidNotReceiveWithAnyArgs()
            .SettleAppendAsync(default!, default, default, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A record nobody is standing for answers <c>200</c> with <c>false</c> rather than an error, because the
    /// operator's question is whether the append is still outstanding — which is what makes the command safe to repeat.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SettleRestoreAppendAsync_OperatorNamedARecord_CarriesTheirVerdictAndReportsWhetherItWasStanding(
        bool wasStanding)
    {
        // Arrange
        var store = Substitute.For<IMailboxRestoreStore>();
        store.SettleAppendAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailAccountId>(),
                Arg.Any<MailboxRestoreAppendId>(),
                Arg.Any<bool>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(wasStanding));

        var record = Guid.CreateVersion7();

        // Act
        var answer = await MailAccountCustodyEndpoints.SettleRestoreAppendAsync(
            new MailAccountRestoreSettlementRequest(Account.Value, record, SourceHoldsTheCopy: true),
            CatalogServing(Account),
            SettlementOver(store),
            TestContext.Current.CancellationToken);

        // Assert
        var settled = Assert.IsType<Ok<MailAccountRestoreSettlementResponse>>(answer.Result).Value;
        Assert.NotNull(settled);
        Assert.Equal(Account.Value, settled.Account);
        Assert.Equal(record, settled.Record);
        Assert.Equal(wasStanding, settled.WasSettled);
        await store.Received(1).SettleAppendAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            new MailboxRestoreAppendId(record),
            true,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    private static MailboxRestoreSettlement SettlementOver(IMailboxRestoreStore store) => new(
        store,
        CommitPolicy(),
        AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminCustodyWrite),
        new FakeTimeProvider(Moment));

    private static MailboxRestorePass RestoreOver(
        MailboxRestoreStanding? standing = null,
        params MailboxRestoreAppend[] unanswered)
    {
        var store = Substitute.For<IMailboxRestoreStore>();
        store.ReadStandingAsync(Arg.Any<MailAccountId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(standing ?? MailboxRestoreStanding.Nothing));
        store.ReadUnansweredAppendsAsync(Arg.Any<MailAccountId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxRestoreAppend>>(unanswered));

        return RestoreOver(store);
    }

    private static MailboxRestorePass RestoreOver(IMailboxRestoreStore store)
    {
        return new MailboxRestorePass(
            Substitute.For<IMailAccountCustodyStore>(),
            store,
            Substitute.For<IMailboxDrainStore>(),
            Substitute.For<IMailboxMutationRecordStore>(),
            Substitute.For<IEmailContentStore>(),
            Substitute.For<IMailboxWriteSessionFactory>(),
            Substitute.For<IMailFolderResolutionStore>(),
            Substitute.For<IMailTransportSecurityPolicyReader>(),
            Substitute.For<IMailFolderMappingReader>(),
            CommitPolicy(),
            new MailboxSynchronizationOptions(),
            new FakeTimeProvider(Moment));
    }

    private static OptimisticConcurrencyRetryPolicy CommitPolicy()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions(),
            new FakeTimeProvider(Moment));
    }

    private static IDeploymentMailAccountCatalog CatalogServing(params MailAccountId[] accounts)
    {
        var catalog = Substitute.For<IDeploymentMailAccountCatalog>();
        catalog.ServedAccounts.Returns(
        [
            .. accounts.Select(account => new ServedMailAccount(
                account,
                MailAccountDisplayName.Create(account.Value),
                MailSynchronizationMode.Polling)),
        ]);

        return catalog;
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
