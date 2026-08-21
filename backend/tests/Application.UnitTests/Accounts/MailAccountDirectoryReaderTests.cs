// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Observability;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts;

public sealed class MailAccountDirectoryReaderTests
{
    private static readonly DateTimeOffset SynchronizedAt = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly ServedMailAccount Work = new(
        MailAccountId.Create("acct-1"),
        MailAccountDisplayName.Create("Work mail"),
        MailSynchronizationMode.Polling);

    private static readonly ServedMailAccount Private = new(
        MailAccountId.Create("acct-2"),
        MailAccountDisplayName.Create("Private mail"),
        MailSynchronizationMode.Push);

    /// <summary>A caller that cannot see the accounts cannot name one, so the names an operator configured are what this publishes.</summary>
    [Fact]
    public async Task ReadAsync_ADeploymentServingTwoAccounts_PublishesBothWithTheNamesConfigurationGaveThem()
    {
        // Arrange
        var reader = ReaderOver(Freshness(), Work, Private);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [Work, Private],
            directory.Accounts.Select(account => account.Account));
    }

    [Fact]
    public async Task ReadAsync_AnAccountWithSynchronizedFolders_ReportsThemOrderedByAlias()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness(
                (Work.Id, "INBOX", SynchronizedAt),
                (Work.Id, "ARCHIVE", null)),
            Work);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var folders = Assert.Single(directory.Accounts).Folders;
        Assert.Equal(
            [("ARCHIVE", null), ("INBOX", SynchronizedAt)],
            folders.Select(folder => (folder.FolderAlias.Value, folder.SynchronizedAt)));
    }

    /// <summary>An account nothing has reached is published with no folder rather than omitted, so it can still be named in a request.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountSynchronizationHasNeverReached_IsPublishedWithNoFolder()
    {
        // Arrange
        var reader = ReaderOver(Freshness((Work.Id, "INBOX", SynchronizedAt)), Work, Private);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(directory.Accounts.Single(account => account.Account == Private).Folders);
    }

    /// <summary>The read is scoped to the served accounts, so a folder of an account an operator removed is not republished here.</summary>
    [Fact]
    public async Task ReadAsync_ADeploymentServingOneAccount_ScopesTheFreshnessReadToIt()
    {
        // Arrange
        var freshnessReader = Freshness();
        var reader = ReaderOver(freshnessReader, Work);

        // Act
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        await freshnessReader
            .Received(1)
            .ReadAsync(
                Arg.Is<MailboxScope>(scope => scope != null && scope.AccountIds.Count == 1 && scope.AccountIds[0] == Work.Id),
                Arg.Any<CancellationToken>());
    }

    /// <summary>A deployment serving nothing asks storage nothing, because there is no scope any folder could belong to.</summary>
    [Fact]
    public async Task ReadAsync_ADeploymentServingNoAccount_PublishesNoneAndReadsNoFreshness()
    {
        // Arrange
        var freshnessReader = Freshness();
        var reader = ReaderOver(freshnessReader);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(directory.Accounts);
        await freshnessReader
            .DidNotReceive()
            .ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Switching synchronization off leaves every account readable, and saying so is what tells a stale copy from a quiet mailbox.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAsync_ADeploymentThatSwitchedSynchronizationOff_StillPublishesItsAccountsAndSaysSo(bool synchronizationEnabled)
    {
        // Arrange
        var reader = ReaderOver(Freshness(), synchronizationEnabled, Work);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(synchronizationEnabled, directory.SynchronizationEnabled);
        Assert.Single(directory.Accounts);
    }

    /// <summary>
    /// The read is reported as the operation it is, so the queries it issues have a use case above them in a trace
    /// rather than only the protocol call that reached it.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AnyRead_ReportsTheDirectoryReadAndHowManyAccountsItPublished()
    {
        // Arrange
        var readTelemetry = new RecordingMailboxReadTelemetry();
        var reader = ReaderOver(readTelemetry, Freshness(), synchronizationEnabled: true, Work, Private);

        // Act
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.Single(readTelemetry.Reads);

        Assert.Equal(MailboxReadOperation.ReadAccountDirectory, read.Operation);
        Assert.Equal(2, read.ResultCount);
        Assert.True(read.WasClosed);
    }

    /// <summary>A deployment serving nothing still reports the read, because a read that happened is not a read that did nothing.</summary>
    [Fact]
    public async Task ReadAsync_ADeploymentServingNoAccount_ReportsTheReadAsAnEmptyOne()
    {
        // Arrange
        var readTelemetry = new RecordingMailboxReadTelemetry();
        var reader = ReaderOver(readTelemetry, Freshness(), synchronizationEnabled: true);

        // Act
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, Assert.Single(readTelemetry.Reads).ResultCount);
    }

    /// <summary>Naming the accounts a deployment serves is publishing that they exist, so the grant is asked for here rather than only where a tool is withheld.</summary>
    [Fact]
    public async Task ReadAsync_ACallerGrantedOnlyTheAnsweringPermission_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var reader = ReaderAuthorizedBy(AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
    }

    /// <summary>Work no caller requested is a distinct kind of principal rather than a caller holding everything.</summary>
    [Fact]
    public async Task ReadAsync_TheProcessIdentity_IsRefused()
    {
        // Arrange
        var reader = ReaderAuthorizedBy(AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Builds a reader whose collaborators would all answer, so a refusal can only have come from the grant.</summary>
    private static MailAccountDirectoryReader ReaderAuthorizedBy(AccessAuthorization authorization)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.SynchronizationEnabled.Returns(true);
        catalog.ServedAccounts.Returns([SyntheticServedAccount.Of("personal")]);

        return new MailAccountDirectoryReader(
            catalog,
            Freshness(),
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Nothing,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            new RecordingMailboxReadTelemetry(),
            authorization);
    }

    private static MailAccountDirectoryReader ReaderOver(
        ISynchronizationFreshnessReader freshnessReader,
        params ServedMailAccount[] servedAccounts) =>
        ReaderOver(freshnessReader, synchronizationEnabled: true, servedAccounts);

    private static MailAccountDirectoryReader ReaderOver(
        ISynchronizationFreshnessReader freshnessReader,
        bool synchronizationEnabled,
        params ServedMailAccount[] servedAccounts) =>
        ReaderOver(new RecordingMailboxReadTelemetry(), freshnessReader, synchronizationEnabled, servedAccounts);

    private static MailAccountDirectoryReader ReaderOver(
        IMailboxReadTelemetry readTelemetry,
        ISynchronizationFreshnessReader freshnessReader,
        bool synchronizationEnabled,
        params ServedMailAccount[] servedAccounts)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.SynchronizationEnabled.Returns(synchronizationEnabled);
        catalog.ServedAccounts.Returns([.. servedAccounts]);

        return new MailAccountDirectoryReader(
            catalog,
            freshnessReader,
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Nothing,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            readTelemetry,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }

    /// <summary>Answers the folders local state knows of, in an order the reader is expected to correct.</summary>
    private static ISynchronizationFreshnessReader Freshness(
        params (MailAccountId AccountId, string FolderAlias, DateTimeOffset? SynchronizedAt)[] folders)
    {
        var freshnessReader = Substitute.For<ISynchronizationFreshnessReader>();
        freshnessReader
            .ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            [
                .. folders.Select(folder => new MailboxFolderFreshness(
                    folder.AccountId,
                    MailFolderAlias.Create(folder.FolderAlias),
                    folder.SynchronizedAt)),
            ]);

        return freshnessReader;
    }
}
