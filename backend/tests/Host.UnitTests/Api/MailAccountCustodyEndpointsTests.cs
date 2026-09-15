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
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the two routes an account's custody is read from and changed on.</summary>
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

    [Fact]
    public async Task ReadAsync_AnAccountThisDeploymentDoesNotServe_IsRefusedAsARequestToCorrect()
    {
        // Act
        var answer = await MailAccountCustodyEndpoints.ReadAsync(
            "absent",
            CatalogServing(Account),
            SwitchOver(CustodyStoreHolding(MailAccountCustodyState.Mirrored)),
            DrainOver(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<ProblemHttpResult>(answer.Result).StatusCode);
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
