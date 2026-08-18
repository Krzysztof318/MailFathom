// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Maintenance;

/// <summary>Covers what asking a deployment to re-read its stored mail records, and what it hands to the queue.</summary>
/// <remarks>
/// The request is the whole of what an operator's terminal does. What it owes is that asking twice is asking once, that
/// the answer says which of the two happened, and that the work is in the queue when it returns — including where an
/// earlier request wrote the run down and never reached the queue at all.
/// </remarks>
public sealed class StoredMailRederivationRequestsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly StoredMailScope WholeAccount = new(MailAccountId.Create("work"), null);

    private readonly InMemoryStoredMailRederivationRunStore runs = new();
    private readonly IJobStore jobs = Substitute.For<IJobStore>();
    private readonly FakeTimeProvider timeProvider = new(Now);

    public StoredMailRederivationRequestsTests() => this.jobs
        .EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
        .Returns(JobEnqueueResult.Created(JobId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001"))));

    /// <summary>A scope nobody has asked about is written down and handed to the queue in one request.</summary>
    [Fact]
    public async Task SubmitAsync_AScopeWithNoRun_RecordsOneAndEnqueuesItsFirstSegment()
    {
        // Act
        var submitted = await this.CreateRequests().SubmitAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(submitted.Accepted);
        Assert.Equal(StoredMailRederivationCarriage.Carried, submitted.Carriage);
        Assert.Equal(Now, submitted.Run.RequestedAt);
        Assert.True(submitted.Run.IsOutstanding);
        Assert.Equal(1, submitted.Run.SegmentCount);

        var request = this.EnqueuedRequests().Single();

        Assert.Equal(JobType.RederiveStoredMail, request.JobType);
        Assert.Equal(WholeAccount.Account, request.AccountId);

        var payload = Assert.IsType<StoredMailScopeJobPayload>(request.Payload);

        Assert.Equal(WholeAccount.Account, payload.ToAccountId());
        Assert.Null(payload.ToFolderAlias());
    }

    /// <summary>Asking twice for one scope is asking once: the second request is answered with the walk already going.</summary>
    [Fact]
    public async Task SubmitAsync_AScopeAlreadyBeingWalked_AnswersWithThatRunAndStartsNoSecondWalk()
    {
        // Arrange
        var requests = this.CreateRequests();
        var first = await requests.SubmitAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Act
        var second = await requests.SubmitAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(second.Accepted);
        Assert.Equal(first.Run.RunId, second.Run.RunId);
        Assert.Single(this.runs.Saves);
    }

    /// <summary>The second request enqueues the same key, which is what repairs a run whose work never reached the queue.</summary>
    /// <remarks>
    /// The run is written and the job enqueued in two commits, so a process that stopped between them leaves a scope
    /// that reads as being walked while nothing is walking it. Asking again is the whole remedy: the key names the
    /// segment the run is on, so a job that is there is answered with itself and one that is not is created.
    /// </remarks>
    [Fact]
    public async Task SubmitAsync_AScopeAlreadyBeingWalked_EnqueuesTheSameSegmentAgain()
    {
        // Arrange
        var requests = this.CreateRequests();

        await requests.SubmitAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Act
        await requests.SubmitAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        var enqueued = this.EnqueuedRequests();

        Assert.Equal(2, enqueued.Count);
        Assert.Equal(enqueued[0].Key.Value, enqueued[1].Key.Value);
    }

    /// <summary>A finished run is not an answer to a new request, because the next release is what the command exists for.</summary>
    [Fact]
    public async Task SubmitAsync_AScopeWhoseLastRunFinished_StartsANewRunUnderAKeyOfItsOwn()
    {
        // Arrange
        var requests = this.CreateRequests();
        var first = await requests.SubmitAsync(WholeAccount, TestContext.Current.CancellationToken);

        this.runs.Arrange(this.runs.Find(WholeAccount)! with { EndedAt = Now });

        // Act
        var second = await requests.SubmitAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(second.Accepted);
        Assert.NotEqual(first.Run.RunId, second.Run.RunId);

        var enqueued = this.EnqueuedRequests();

        Assert.NotEqual(enqueued[0].Key.Value, enqueued[1].Key.Value);
    }

    /// <summary>Two scopes are two runs, so refreshing one folder says nothing about the account's own walk.</summary>
    [Fact]
    public async Task SubmitAsync_OneFolderOfAnAccountBeingWalked_StartsARunOfItsOwn()
    {
        // Arrange
        var requests = this.CreateRequests();
        StoredMailScope inbox = new(WholeAccount.Account, MailFolderAlias.Create("inbox"));

        await requests.SubmitAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Act
        var narrowed = await requests.SubmitAsync(inbox, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(narrowed.Accepted);
        Assert.Equal(inbox, narrowed.Run.Scope);
        Assert.Equal(2, this.runs.Saves.Count);
    }

    /// <summary>The key fits the bound whatever an operator named their folders, which is why the account is not in it.</summary>
    [Fact]
    public void KeyOf_AFolderNamedAtItsGreatestLength_ComposesAKeyInsideTheBound()
    {
        // Arrange
        var longestName = new string('a', 128);
        var folder = MailFolderAlias.Create(longestName);

        StoredMailRederivationRun run = new()
        {
            RunId = StoredMailRederivationRunId.Create(Guid.Parse("0199a0c0-0000-7000-8000-00000000000f")),
            Scope = new StoredMailScope(MailAccountId.Create(longestName), folder),
            RequestedAt = Now,
            SegmentCount = int.MaxValue,
        };

        // Act
        var key = StoredMailRederivationRequests.KeyOf(run);

        // Assert
        Assert.True(key.Value.Length <= JobIdempotencyKey.MaximumLength);
        Assert.Contains(folder.Value, key.Value, StringComparison.Ordinal);
    }

    /// <summary>A full queue is backpressure the operator acts on, and the run it recorded stands.</summary>
    [Fact]
    public async Task SubmitAsync_AQueueAtItsBound_ReportsTheRefusalAndKeepsTheRun()
    {
        // Arrange
        this.jobs
            .EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(JobEnqueueResult.RefusedAtCapacity());

        // Act
        var submitted = await this.CreateRequests().SubmitAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(submitted.Accepted);
        Assert.Equal(StoredMailRederivationCarriage.QueueAtCapacity, submitted.Carriage);
        Assert.NotNull(this.runs.Find(WholeAccount));
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public async Task SubmitAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var requests = this.CreateRequests(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            requests.SubmitAsync(WholeAccount, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
        Assert.Empty(this.runs.Saves);
        Assert.Empty(this.EnqueuedRequests());
    }

    private IReadOnlyList<JobEnqueueRequest> EnqueuedRequests() =>
    [
        .. this.jobs.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IJobStore.EnqueueAsync))
            .Select(call => (JobEnqueueRequest)call.GetArguments()[0]!),
    ];

    private StoredMailRederivationRequests CreateRequests(AccessAuthorization? authorization = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new StoredMailRederivationRequests(
            this.runs,
            this.jobs,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                this.timeProvider),
            this.timeProvider,
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
