// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>Discards synchronization progress against a real database, which is where the removal's narrowing is decided.</summary>
/// <remarks>
/// <para>
/// The rewind's whole contract is a query: it joins each checkpoint to its folder, keeps the ones whose account and —
/// where the operator named one — whose alias match, removes exactly those rows, and answers with their aliases. Every
/// part of that is translated to SQL, so a hand-written fake proves the use case's arrangement and nothing about
/// whether the narrowing holds. What rests on it is the rewind's safety property: a run in flight is refused its
/// advance because the row it decided from is gone, and a removal that took the wrong rows would refuse the wrong runs.
/// </para>
/// <para>
/// Two folders this class owns, and never the account-wide shape. One synthetic account carries every class in this
/// collection, so a whole-account removal would discard the progress of folders other classes seeded and left, and
/// whichever of them ran next would fail on state this test took away. The account filter is asserted instead through
/// an account the deployment stores nothing for, which reaches the same predicate over rows nobody owns.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
[TestCaseOrderer(typeof(MailboxStateSequenceOrderer))]
public sealed class OrchestratedMailSynchronizationRewindTests(MailFathomOrchestrationFixture orchestration)
{
    private const string RewoundFolderName = "Rewound";
    private const string KeptFolderName = "RewindKept";

    private static readonly MailFolderMapping RewoundFolder = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("rewound"),
        RemotePathOf(RewoundFolderName));

    private static readonly MailFolderMapping KeptFolder = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("rewind-kept"),
        RemotePathOf(KeptFolderName));

    /// <summary>Seeds both folders and synchronizes them, so each binding holds progress the removal can be read against.</summary>
    [Fact]
    [MailboxStateStep(1)]
    public async Task SynchronizeAsync_OverBothFoldersThisClassOwns_LeavesEachOfThemHoldingProgress()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        foreach (var folderName in new[] { RewoundFolderName, KeptFolderName })
        {
            await mailbox.RecreateFolderAsync(folderName, cancellationToken);
            await mailbox.AppendAsync(folderName, $"rewind-{folderName}", cancellationToken);
        }

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var rewoundFolderRun = await SynchronizeAsync(services, RewoundFolder, cancellationToken);
        var keptFolderRun = await SynchronizeAsync(services, KeptFolder, cancellationToken);

        // Assert
        Assert.Equal(MailboxSynchronizationOutcome.Synchronized, rewoundFolderRun.Outcome);
        Assert.Equal(MailboxSynchronizationOutcome.Synchronized, keptFolderRun.Outcome);
        Assert.NotNull(await ReadCheckpointAsync(services, RewoundFolder.Alias, cancellationToken));
        Assert.NotNull(await ReadCheckpointAsync(services, KeptFolder.Alias, cancellationToken));
    }

    /// <summary>The alias an operator names is the whole of what is discarded, and the account's other folders resume where they were.</summary>
    [Fact]
    [MailboxStateStep(2)]
    public async Task RewindAsync_AScopeNarrowedToOneAlias_DiscardsThatBindingAndLeavesTheOtherFoldersProgress()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = new StoredMailScope(SyntheticMailAccount.AccountId, RewoundFolder.Alias);

        // Act
        var rewound = await services.InScopeAsync(
            (serviceScope, token) => serviceScope.GetRequiredService<MailSynchronizationRewind>()
                .RewindAsync(scope, token),
            cancellationToken);

        // Assert
        Assert.Equal([RewoundFolder.Alias], rewound);
        Assert.Null(await ReadCheckpointAsync(services, RewoundFolder.Alias, cancellationToken));
        Assert.NotNull(await ReadCheckpointAsync(services, KeptFolder.Alias, cancellationToken));
    }

    /// <summary>
    /// The account is the other half of the same predicate. An account this deployment stores nothing for matches no
    /// binding, so the removal answers with no folder and takes nothing — which is what says the query narrowed by the
    /// account rather than by the alias alone.
    /// </summary>
    [Fact]
    [MailboxStateStep(3)]
    public async Task RewindAsync_AnAccountNothingIsStoredFor_DiscardsNothingAtAll()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scope = new StoredMailScope(MailAccountId.Create("rewind-unknown-account"), null);

        // Act
        var rewound = await services.InScopeAsync(
            (serviceScope, token) => serviceScope.GetRequiredService<MailSynchronizationRewind>()
                .RewindAsync(scope, token),
            cancellationToken);

        // Assert
        Assert.Empty(rewound);
        Assert.NotNull(await ReadCheckpointAsync(services, KeptFolder.Alias, cancellationToken));
    }

    private static RemoteFolderPath RemotePathOf(string folderName) =>
        RemoteFolderPath.Create(folderName, hierarchyDelimiter: '.');

    private static Task<MailboxSynchronizationResult> SynchronizeAsync(
        OrchestratedMailFathomServices services,
        MailFolderMapping mapping,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxSynchronizer>().SynchronizeAsync(
                SyntheticMailAccount.AccountId,
                mapping,
                token),
            cancellationToken);

    private static Task<SynchronizationCheckpoint?> ReadCheckpointAsync(
        OrchestratedMailFathomServices services,
        MailFolderAlias alias,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var resolution = await scope.GetRequiredService<IMailFolderResolutionStore>()
                    .GetCurrentResolutionAsync(SyntheticMailAccount.AccountId, alias, token);

                return resolution is null
                    ? null
                    : await scope.GetRequiredService<ISynchronizationCheckpointStore>().GetCheckpointAsync(
                        SyntheticMailAccount.AccountId,
                        resolution.Id,
                        token);
            },
            cancellationToken);
}
