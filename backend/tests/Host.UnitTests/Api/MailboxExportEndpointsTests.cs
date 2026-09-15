// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Export;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Exports;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the seven routes a mailbox leaves this deployment through.</summary>
/// <remarks>
/// What is asserted here is what a caller is answered rather than what the archive holds. The status a refusal reaches
/// the operator under is the whole of this: an export nothing holds and an archive that has gone are different
/// situations with different remedies, and a command that met one status for both would tell an operator to look for an
/// identity they typed correctly.
/// </remarks>
public sealed class MailboxExportEndpointsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly Guid ExportId = new("0199a0c0-0000-7000-8000-000000000007");

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes every one of
    /// these paths from constants of its own, and a rename on either side compiles cleanly while the command reaches a
    /// 404 that reads exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void MailboxExportRoutes_AreThePathsTheCommandComposes()
    {
        Assert.Equal("/exports", MailboxExportEndpoints.ExportsRoute);
        Assert.Equal("/exports/measurement", MailboxExportEndpoints.MeasurementRoute);
        Assert.Equal("/exports/{exportId:guid}", MailboxExportEndpoints.ExportRoute);
        Assert.Equal("/exports/{exportId:guid}/cancellation", MailboxExportEndpoints.CancellationRoute);
        Assert.Equal("/exports/{exportId:guid}/archive", MailboxExportEndpoints.ArchiveRoute);
    }

    /// <summary>
    /// The measurement sits beneath the collection, so the identity constraint is what keeps the two apart. Without it
    /// a measurement would be read as an export named <c>measurement</c> and answered 404.
    /// </summary>
    [Fact]
    public void MeasurementRoute_AndTheRouteOneExportIsReadFrom_AreKeptApartByTheIdentityConstraint()
    {
        Assert.StartsWith(
            $"{MailboxExportEndpoints.ExportsRoute}/",
            MailboxExportEndpoints.MeasurementRoute,
            StringComparison.Ordinal);
        Assert.Contains(":guid", MailboxExportEndpoints.ExportRoute, StringComparison.Ordinal);
    }

    /// <summary>An account this deployment does not serve is refused before any mailbox is read, and the refusal names it.</summary>
    [Fact]
    public async Task MeasureAsync_AnAccountThisDeploymentDoesNotServe_IsRefusedBeforeAnythingIsMeasured()
    {
        // Arrange
        var reader = Substitute.For<IMailboxExportReader>();

        // Act
        var result = await MailboxExportEndpoints.MeasureAsync(
            "somebody-elses",
            folder: null,
            CatalogServing(Account),
            ExportsOver(reader: reader),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        await reader.DidNotReceive()
            .MeasureAsync(Arg.Any<MailAccountId>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A deployment keeping its content in the database has nowhere to put an archive, and the refusal is about the
    /// deployment's configuration rather than about the request — so it is a request this deployment will not serve.
    /// </summary>
    [Fact]
    public async Task MeasureAsync_ADeploymentWithNoObjectBackend_IsRefusedAsARequestItWillNotServe()
    {
        // Arrange
        var archives = Substitute.For<IMailboxExportArchiveStore>();
        archives.IsAvailable.Returns(false);

        // Act
        var result = await MailboxExportEndpoints.MeasureAsync(
            Account.Value,
            folder: null,
            CatalogServing(Account),
            ExportsOver(archives: archives),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains(
            MailboxExports.ObjectStorageSettingName,
            refusal.ProblemDetails.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An identity this account holds nothing under is <c>404</c>, because the remedy is a different identity. Reporting
    /// it as anything else would send an operator looking for a deployment problem they do not have.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AnExportTheAccountDoesNotHold_IsAnsweredAsNotFound()
    {
        // Arrange
        var store = Substitute.For<IMailboxExportStore>();
        store.FindAsync(Arg.Any<MailAccountId>(), Arg.Any<MailboxExportId>(), Arg.Any<CancellationToken>())
            .Returns((MailboxExport?)null);

        // Act
        var result = await MailboxExportEndpoints.ReadAsync(
            ExportId,
            Account.Value,
            CatalogServing(Account),
            ExportsOver(store: store),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, refusal.StatusCode);
    }

    /// <summary>
    /// An archive that has expired or been deleted is <c>409</c> rather than <c>404</c>: the export is there and the
    /// identity is right, and what the operator does about it is ask for another export.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_AnExportWhoseArchiveIsGone_IsAnsweredAsAConflictRatherThanAsNotFound()
    {
        // Arrange
        var store = Substitute.For<IMailboxExportStore>();
        store.FindAsync(Arg.Any<MailAccountId>(), Arg.Any<MailboxExportId>(), Arg.Any<CancellationToken>())
            .Returns(Expired());

        // Act
        var result = await MailboxExportEndpoints.DownloadAsync(
            ExportId,
            Account.Value,
            CatalogServing(Account),
            ExportsOver(store: store),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, refusal.StatusCode);
    }

    /// <summary>
    /// An account already writing a different scope is <c>409</c> too, and for the same reason: nothing about the
    /// request is wrong, the deployment is simply in a state this request cannot have.
    /// </summary>
    [Fact]
    public async Task StartAsync_AnAccountAlreadyWritingADifferentScope_IsAnsweredAsAConflict()
    {
        // Arrange
        var store = Substitute.For<IMailboxExportStore>();
        store.FindInFlightAsync(Arg.Any<MailAccountId>(), Arg.Any<CancellationToken>())
            .Returns(Running(folderPath: "Archive"));

        // Act
        var result = await MailboxExportEndpoints.StartAsync(
            new MailboxExportRequest(Account.Value, Folder: null),
            CatalogServing(Account),
            ExportsOver(store: store),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, refusal.StatusCode);
    }

    /// <summary>
    /// The archive is served as the store's own stream under a name carrying nothing but the export's identity: a file
    /// name travels into a downloads directory, a shell history, and a backup index.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_AFinishedArchive_ServesItAsAZipNamedAfterNothingButTheExport()
    {
        // Arrange
        var store = Substitute.For<IMailboxExportStore>();
        store.FindAsync(Arg.Any<MailAccountId>(), Arg.Any<MailboxExportId>(), Arg.Any<CancellationToken>())
            .Returns(Finished());

        var archives = Substitute.For<IMailboxExportArchiveStore>();
        archives.IsAvailable.Returns(true);
        archives.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3, 4])));

        // Act
        var result = await MailboxExportEndpoints.DownloadAsync(
            ExportId,
            Account.Value,
            CatalogServing(Account),
            ExportsOver(store: store, archives: archives),
            TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.IsType<FileStreamHttpResult>(result.Result);
        Assert.Equal("application/zip", served.ContentType);
        Assert.Equal($"mailfathom-export-{ExportId:N}.zip", served.FileDownloadName);
        Assert.DoesNotContain(Account.Value, served.FileDownloadName, StringComparison.OrdinalIgnoreCase);
    }

    private static MailboxExport Finished() => new(
        MailboxExportId.Create(ExportId),
        Account,
        FolderPath: null,
        MailboxExportState.Completed,
        RequestedAt: Moment,
        MessageCount: 4,
        ByteCount: 40_960,
        ArchiveByteLength: 20_480,
        ObjectLocator: "mailbox-exports/written",
        CompletedAt: Moment,
        ExpiresAt: Moment.AddHours(48),
        FailureCode: null);

    private static MailboxExport Expired() => Finished() with
    {
        State = MailboxExportState.Expired,
        ObjectLocator = null,
        ExpiresAt = null,
    };

    private static MailboxExport Running(string folderPath) => Finished() with
    {
        State = MailboxExportState.Running,
        FolderPath = folderPath,
        ArchiveByteLength = null,
        ObjectLocator = null,
        CompletedAt = null,
        ExpiresAt = null,
    };

    /// <summary>Composes the use case the routes reach, over ports this suite states and with the grant granted.</summary>
    private static MailboxExports ExportsOver(
        IMailboxExportStore? store = null,
        IMailboxExportReader? reader = null,
        IMailboxExportArchiveStore? archives = null)
    {
        var archiveStore = archives ?? Substitute.For<IMailboxExportArchiveStore>();

        if (archives is null)
        {
            archiveStore.IsAvailable.Returns(true);
        }

        return new MailboxExports(
            AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminExport),
            store ?? Substitute.For<IMailboxExportStore>(),
            reader ?? Substitute.For<IMailboxExportReader>(),
            archiveStore,
            Substitute.For<IMailboxExportAuditor>(),
            Substitute.For<IJobStore>(),
            new StoredContentCeiling(Substitute.For<IStoredContentClaimStore>(), ceilingBytes: null),
            MailboxExportSettings.Default,
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
}
