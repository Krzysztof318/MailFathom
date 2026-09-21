// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.StoredFiles;

/// <summary>
/// Covers the sweep of stored files no record links to. What it has to hold is that a file its owner's record links to
/// is left alone however the candidate query named it, that a file too young to judge is never asked about, and that
/// a replica refused the lease removes nothing.
/// </summary>
public sealed class UnlinkedStoredFileSweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly StoredFileId Linked = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000001"));

    private static readonly StoredFileId Unlinked = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000002"));

    /// <summary>The candidate query reads a record's text, so the record's own reading of its links is what a removal rests on.</summary>
    [Fact]
    public async Task RunAsync_ACandidateItsOwnersRecordLinks_IsLeftAloneWhileTheUnlinkedOneIsRemoved()
    {
        // Arrange
        var files = FilesNaming(new HeldStoredFile(Linked, SyntheticUser.Deployment), new HeldStoredFile(Unlinked, SyntheticUser.Deployment));
        var links = Substitute.For<IUserRecordFileLinks>();
        links.ReadLinkedFilesAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>())
            .Returns(new HashSet<StoredFileId> { Linked });

        var sweep = Sweeping(files, links, new GrantingLeaseRunner());

        // Act
        var removed = await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, removed);
        await files.Received(1).RemoveAsync(SyntheticUser.Deployment, Unlinked, Arg.Any<CancellationToken>());
        await files.DidNotReceive().RemoveAsync(Arg.Any<UserId>(), Linked, Arg.Any<CancellationToken>());
    }

    /// <summary>A file younger than the floor may be a write whose link has not committed yet.</summary>
    [Fact]
    public async Task RunAsync_Always_AsksOnlyForFilesOlderThanTheAgeFloor()
    {
        // Arrange
        var files = FilesNaming();
        var sweep = Sweeping(files, Substitute.For<IUserRecordFileLinks>(), new GrantingLeaseRunner());

        // Act
        await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        await files.Received(1).FindUnmentionedAsync(
            Now - UnlinkedStoredFileSweep.MinimumFileAge,
            UnlinkedStoredFileSweep.MaximumFilesPerRun,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AReplicaRefusedTheLease_RemovesNothingAndSaysSo()
    {
        // Arrange
        var files = FilesNaming(new HeldStoredFile(Unlinked, SyntheticUser.Deployment));
        var runner = Substitute.For<IWorkLeaseRunner>();
        runner.TryRunUnderLeaseAsync(Arg.Any<WorkScope>(), Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var sweep = Sweeping(files, Substitute.For<IUserRecordFileLinks>(), runner);

        // Act
        var removed = await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(removed);
        await files.DidNotReceive().RemoveAsync(Arg.Any<UserId>(), Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ACallerRatherThanTheProcess_IsRefused()
    {
        // Arrange
        var sweep = new UnlinkedStoredFileSweep(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
            FilesNaming(),
            Substitute.For<IUserRecordFileLinks>(),
            new GrantingLeaseRunner(),
            new FakeTimeProvider(Now));

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => sweep.RunAsync(TestContext.Current.CancellationToken));
    }

    private static IStoredFileStore FilesNaming(params HeldStoredFile[] candidates)
    {
        var files = Substitute.For<IStoredFileStore>();
        files.FindUnmentionedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(candidates);

        return files;
    }

    private static UnlinkedStoredFileSweep Sweeping(
        IStoredFileStore files,
        IUserRecordFileLinks links,
        IWorkLeaseRunner leases) =>
        new(
            AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process),
            files,
            links,
            leases,
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
}
