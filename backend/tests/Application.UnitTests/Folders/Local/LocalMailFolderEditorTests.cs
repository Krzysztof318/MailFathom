// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Folders.Local;

/// <summary>
/// Covers the folder acts a person takes on an account whose mailbox MailFathom holds: that each is refused on any other
/// account and without its own grant, that the protected folders are there for the first act, and that a committed act is
/// audited, signalled, and — for an erasure — followed by the erasure of the folder's mail.
/// </summary>
public sealed class LocalMailFolderEditorTests
{
    private static readonly MailAccountId Account =
        MailAccountId.Create("primary");

    [Fact]
    public async Task CreateAsync_TheFirstActOnAHeldAccount_CreatesTheFolderBesideTheFiveProtectedOnes()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);

        // Act
        var outcome = await deployment.Editor.CreateAsync(Account, parentId: null, "Projects", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Created, outcome.Kind);
        Assert.Equal(
            ["Drafts", "INBOX", "Junk", "Projects", "Sent", "Trash"],
            deployment.Store.Folders.Select(folder => folder.Name.Value).Order(StringComparer.Ordinal));
    }

    /// <summary>A mirrored account's folders are the source server's, and a restoring one is on its way back there.</summary>
    [Theory]
    [InlineData(MailAccountCustodyPhase.Mirrored)]
    [InlineData(MailAccountCustodyPhase.Restoring)]
    public async Task CreateAsync_AnAccountNotHeld_IsRefusedAndWritesNothing(MailAccountCustodyPhase phase)
    {
        // Arrange
        await using var deployment = new EditorDeployment(phase);

        // Act
        var outcome = await deployment.Editor.CreateAsync(Account, parentId: null, "Projects", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.AccountNotHeld, outcome.Refusal);
        Assert.Equal(0, deployment.Store.SaveCount);
        await deployment.Auditor.DidNotReceive().RecordAsync(Arg.Any<LocalMailFolderChange>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_AnAccountTheCallerDoesNotHold_IsRefusedAsMissing()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);

        // Act
        var outcome = await deployment.Editor.CreateAsync(
            MailAccountId.Create("elsewhere"),
            parentId: null,
            "Projects",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.AccountMissing, outcome.Refusal);
    }

    [Fact]
    public async Task CreateAsync_ACallerWhoMayOnlyRead_IsRefusedTheFolderWrite()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held, MailFathomPermission.MailRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            deployment.Editor.CreateAsync(Account, parentId: null, "Projects", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailFoldersWrite, refusal.RequiredPermission);
        Assert.Equal(0, deployment.Store.SaveCount);
    }

    [Fact]
    public async Task RenameAsync_AFolderTheAccountHolds_ReportsARename()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);
        var created = await deployment.CreateAsync("Projects");

        // Act
        var outcome = await deployment.Editor.RenameAsync(Account, created.Id, "Clients", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Renamed, outcome.Kind);
        Assert.Equal("Clients", outcome.Folder!.Name.Value);
    }

    [Fact]
    public async Task MoveAsync_AFolderBeneathAnother_ReportsAMove()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);
        var projects = await deployment.CreateAsync("Projects");
        var clients = await deployment.CreateAsync("Clients");

        // Act
        var outcome = await deployment.Editor.MoveAsync(Account, clients.Id, projects.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Moved, outcome.Kind);
        Assert.Equal(projects.Id, outcome.Folder!.ParentId);
    }

    /// <summary>A folder moved beneath one already in the trash is in the trash too, so the act is reported as the deletion it amounts to.</summary>
    [Fact]
    public async Task MoveAsync_BeneathAFolderWithinTheTrash_ReportsAMoveToTheTrash()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);
        var projects = await deployment.CreateAsync("Projects");
        var old = await deployment.CreateAsync("Old");
        await deployment.Editor.DeleteAsync(Account, old.Id, TestContext.Current.CancellationToken);

        // Act
        var outcome = await deployment.Editor.MoveAsync(Account, projects.Id, old.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.MovedToTrash, outcome.Kind);
        Assert.Equal(old.Id, outcome.Folder!.ParentId);
    }

    [Fact]
    public async Task DeleteAsync_AFolderOutsideTheTrash_MovesItThereAndQueuesNoErasure()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);
        var projects = await deployment.CreateAsync("Projects");

        // Act
        var outcome = await deployment.Editor.DeleteAsync(Account, projects.Id, TestContext.Current.CancellationToken);

        // Assert
        var trash = Assert.Single(deployment.Store.Folders, folder => folder.Role == MailFolderSpecialUse.Trash);

        Assert.Equal(MailFolderChangeKind.MovedToTrash, outcome.Kind);
        Assert.Equal(trash.Id, outcome.Folder!.ParentId);
        await deployment.Jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Deleting what is already in the trash is the erasure, and its mail is left to the bounded passes the queue runs.</summary>
    [Fact]
    public async Task DeleteAsync_AFolderInTheTrash_ErasesItAuditsItAndQueuesTheErasureOfItsMail()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);
        var projects = await deployment.CreateAsync("Projects");
        await deployment.Editor.DeleteAsync(Account, projects.Id, TestContext.Current.CancellationToken);

        // Act
        var outcome = await deployment.Editor.DeleteAsync(Account, projects.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Erased, outcome.Kind);
        Assert.Equal([projects.Id], deployment.Store.Erased);
        await deployment.Auditor.Received(1).RecordAsync(
            Arg.Is<LocalMailFolderChange>(change => change!.Kind == MailFolderChangeKind.Erased
                && change.Folder == projects.Id
                && change.ErasedFolderCount == 1),
            Arg.Any<CancellationToken>());
        await deployment.Jobs.Received(1).EnqueueAsync(
            Arg.Is<JobEnqueueRequest>(request => IsFirstErasurePassOf(request, projects.Id)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A full queue writes no pass, and the erasure that already committed says so rather than reading as complete.</summary>
    [Fact]
    public async Task DeleteAsync_AnErasureTheQueueIsTooFullToTake_ReportsTheMailErasureDeferred()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);
        deployment.Jobs.EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(JobEnqueueResult.RefusedAtCapacity());
        var projects = await deployment.CreateAsync("Projects");
        await deployment.Editor.DeleteAsync(Account, projects.Id, TestContext.Current.CancellationToken);

        // Act
        var outcome = await deployment.Editor.DeleteAsync(Account, projects.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Erased, outcome.Kind);
        Assert.True(outcome.MailErasureDeferred);
    }

    [Fact]
    public async Task DeleteAsync_AnErasureTheQueueTakes_ReportsNothingDeferred()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);
        deployment.Jobs.EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(JobEnqueueResult.Created(JobId.Create(Guid.CreateVersion7())));
        var projects = await deployment.CreateAsync("Projects");
        await deployment.Editor.DeleteAsync(Account, projects.Id, TestContext.Current.CancellationToken);

        // Act
        var outcome = await deployment.Editor.DeleteAsync(Account, projects.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.MailErasureDeferred);
    }

    /// <summary>The audit record names the act that was asked for, so a rename that leaves the text as it was is still a rename.</summary>
    [Fact]
    public async Task RenameAsync_ToTheNameTheFolderAlreadyCarries_ReportsARename()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);
        var projects = await deployment.CreateAsync("Projects");

        // Act
        var outcome = await deployment.Editor.RenameAsync(Account, projects.Id, " Projects ", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Renamed, outcome.Kind);
    }

    /// <summary>A held account nobody has acted on yet still lists its inbox, drafts, sent, junk, and trash, and a second read writes nothing more.</summary>
    [Fact]
    public async Task ReadAsync_AHeldAccountNothingHasActedOn_ListsAndKeepsTheFiveProtectedFolders()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);

        // Act
        var first = await deployment.Editor.ReadAsync(Account, TestContext.Current.CancellationToken);
        var second = await deployment.Editor.ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["Drafts", "INBOX", "Junk", "Sent", "Trash"],
            first!.Folders.Select(folder => folder.Name.Value).Order(StringComparer.Ordinal));
        Assert.Equal(
            first.Folders.Select(folder => folder.Id.Value).Order(),
            second!.Folders.Select(folder => folder.Id.Value).Order());
        Assert.Equal(1, deployment.Store.SaveCount);
    }

    private static bool IsFirstErasurePassOf(JobEnqueueRequest? request, LocalMailFolderId folder) =>
        request?.Payload is EraseLocalMailFolderMailJobPayload { Pass: 0 } payload && payload.FolderId == folder.Value;

    [Fact]
    public async Task CreateAsync_ACommittedAct_TellsTheCallersClientsTheFoldersChanged()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);

        // Act
        await deployment.CreateAsync("Projects");
        deployment.Clock.Advance(ClientSignals.FoldingWindow);
        await deployment.Signals.DrainAsync();

        // Assert
        var signal = Assert.Single(deployment.Channel.Published);

        Assert.Equal(ClientSignal.FoldersChanged(Account).Scope, signal.Scope);
    }

    /// <summary>The editor over an in-memory store, reached by a caller holding what a test states — both folder grants unless it says otherwise.</summary>
    private sealed class EditorDeployment : IAsyncDisposable
    {
        internal EditorDeployment(MailAccountCustodyPhase phase, params MailFathomPermission[] granted)
        {
            this.Store = new InMemoryLocalMailFolderStore(Account, phase);
            this.Signals = new ClientSignals([this.Channel], this.Clock);

            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

            this.Editor = new LocalMailFolderEditor(
                this.Store,
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.Clock),
                this.Auditor,
                this.Signals,
                this.Jobs,
                AccessAuthorizations.ForUserGranted(
                    SyntheticUser.Deployment,
                    granted.Length > 0 ? granted : [MailFathomPermission.MailRead, MailFathomPermission.MailFoldersWrite]),
                this.Clock);
        }

        internal FakeTimeProvider Clock { get; } = new();

        internal InMemoryLocalMailFolderStore Store { get; }

        internal ILocalMailFolderChangeAuditor Auditor { get; } = Substitute.For<ILocalMailFolderChangeAuditor>();

        internal IJobStore Jobs { get; } = Substitute.For<IJobStore>();

        internal RecordingClientSignalChannel Channel { get; } = new();

        internal ClientSignals Signals { get; }

        internal LocalMailFolderEditor Editor { get; }

        /// <summary>Creates a top-level folder a test goes on to act on.</summary>
        internal async Task<LocalMailFolder> CreateAsync(string name) =>
            (await this.Editor.CreateAsync(Account, parentId: null, name, TestContext.Current.CancellationToken)).Folder!;

        public ValueTask DisposeAsync() => this.Signals.DisposeAsync();
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
