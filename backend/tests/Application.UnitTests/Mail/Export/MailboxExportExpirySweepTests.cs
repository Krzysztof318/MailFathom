// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Application.Mail.Export;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Exports;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Export;

public sealed class MailboxExportExpirySweepTests
{
    private static readonly MailAccountId Account = SyntheticMailAccount.Deployment;

    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_AnArchiveWhoseRetentionHasRunOut_RecordsTheExpiryThenDeletesTheObject()
    {
        // Arrange
        var due = Completed(expiresAt: Now.AddMinutes(-1));
        var store = new InMemoryMailboxExportStore().Holding(due);
        var archives = new InMemoryMailboxExportArchiveStore();
        var auditor = new RecordingMailboxExportAuditor();
        var sweep = SweepOver(store, archives, auditor);

        // Act
        var expired = await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, expired);

        var swept = store.Find(due.Id);
        Assert.Equal(MailboxExportState.Expired, swept?.State);
        Assert.Null(swept?.ObjectLocator);
        Assert.Equal(["mailbox-exports/expiring"], archives.Deleted);
        Assert.Contains(auditor.Acts, act => act.Kind == MailboxExportActKind.Expired);
    }

    [Fact]
    public async Task RunAsync_AnArchiveStillInsideItsRetention_LeavesItDownloadable()
    {
        // Arrange
        var held = Completed(expiresAt: Now.AddHours(1));
        var store = new InMemoryMailboxExportStore().Holding(held);
        var archives = new InMemoryMailboxExportArchiveStore();
        var sweep = SweepOver(store, archives);

        // Act
        var expired = await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, expired);
        Assert.Equal(MailboxExportState.Completed, store.Find(held.Id)?.State);
        Assert.Empty(archives.Deleted);
    }

    /// <summary>An export already deleted by hand is somebody else's to remove, and removing it twice is not this pass's to decide.</summary>
    [Fact]
    public async Task RunAsync_AnExportDeletedWhileThePassWasDeciding_DeletesNothingAndCountsNothing()
    {
        // Arrange
        var due = Completed(expiresAt: Now.AddMinutes(-1));
        var store = new StaleReadingExportStore(due);
        var archives = new InMemoryMailboxExportArchiveStore();
        var sweep = SweepOver(store, archives);

        // Act
        var expired = await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, expired);
        Assert.Empty(archives.Deleted);
    }

    [Fact]
    public async Task RunAsync_AReplicaRefusedTheLease_DeletesNothingAndSaysSo()
    {
        // Arrange
        var due = Completed(expiresAt: Now.AddMinutes(-1));
        var archives = new InMemoryMailboxExportArchiveStore();
        var leases = Substitute.For<IWorkLeaseRunner>();
        leases.TryRunUnderLeaseAsync(
            Arg.Any<WorkScope>(),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<CancellationToken>())
            .Returns(false);

        var sweep = SweepOver(new InMemoryMailboxExportStore().Holding(due), archives, leases: leases);

        // Act
        var expired = await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(expired);
        Assert.Empty(archives.Deleted);
    }

    [Fact]
    public async Task RunAsync_ACallerRatherThanTheProcess_IsRefused()
    {
        // Arrange
        var sweep = new MailboxExportExpirySweep(
            AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminExport),
            new InMemoryMailboxExportStore(),
            new InMemoryMailboxExportArchiveStore(),
            new RecordingMailboxExportAuditor(),
            new GrantingLeaseRunner(),
            new FakeTimeProvider(Now));

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            sweep.RunAsync(TestContext.Current.CancellationToken));
    }

    private static MailboxExport Completed(DateTimeOffset expiresAt) => new(
        MailboxExportId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000000cc")),
        Account,
        FolderPath: null,
        MailboxExportState.Completed,
        Now.AddHours(-49),
        MessageCount: 3,
        ByteCount: 300,
        ArchiveByteLength: 280,
        ObjectLocator: "mailbox-exports/expiring",
        CompletedAt: Now.AddHours(-49),
        ExpiresAt: expiresAt,
        FailureCode: null);

    private static MailboxExportExpirySweep SweepOver(
        IMailboxExportStore store,
        InMemoryMailboxExportArchiveStore archives,
        RecordingMailboxExportAuditor? auditor = null,
        IWorkLeaseRunner? leases = null) =>
        new(
            AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process),
            store,
            archives,
            auditor ?? new RecordingMailboxExportAuditor(),
            leases ?? new GrantingLeaseRunner(),
            new FakeTimeProvider(Now));

    /// <summary>Stands in for the lease table on a replica nothing competes with.</summary>
    private sealed class GrantingLeaseRunner : IWorkLeaseRunner
    {
        public async Task<bool> TryRunUnderLeaseAsync(
            WorkScope scope,
            Func<CancellationToken, Task> work,
            CancellationToken cancellationToken)
        {
            await work(cancellationToken);

            return true;
        }
    }

    /// <summary>Names an export as due and then refuses the write, which is what a row somebody else moved does.</summary>
    private sealed class StaleReadingExportStore(MailboxExport due) : IMailboxExportStore
    {
        public Task RecordAsync(MailboxExport queuedExport, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<MailboxExport?> FindAsync(
            MailAccountId account,
            MailboxExportId exportId,
            CancellationToken cancellationToken) =>
            Task.FromResult<MailboxExport?>(null);

        public Task<MailboxExport?> FindInFlightAsync(MailAccountId account, CancellationToken cancellationToken) =>
            Task.FromResult<MailboxExport?>(null);

        public Task<IReadOnlyList<MailboxExport>> ListAsync(
            MailAccountId account,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MailboxExport>>([]);

        public Task<IReadOnlyList<MailboxExport>> FindDueForExpiryAsync(
            DateTimeOffset asOf,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MailboxExport>>([due]);

        public Task<bool> SaveAsync(
            MailboxExport updatedExport,
            MailboxExportState expectedState,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task SaveProgressAsync(
            MailboxExportId exportId,
            long messageCount,
            long byteCount,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
