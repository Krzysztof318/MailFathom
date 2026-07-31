// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Folders;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>Runs whole synchronizations against a real mail server and a real database, twice over the same folder.</summary>
/// <remarks>
/// The two runs are two tests rather than one, and they are ordered, because what the second one asserts is a property
/// of the first one's outcome: that repeating the run stores nothing again and leaves the committed checkpoint where it
/// was. Written as a single test, the second run's failure would be reported against arrangement code; written without
/// an order, whichever ran first would decide what the other found in the folder.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
[TestCaseOrderer(typeof(MailboxStateSequenceOrderer))]
public sealed class OrchestratedMailboxSynchronizationTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The folder this class owns, so its two runs are not disturbed by mail another test delivers to the inbox.</summary>
    private const string SynchronizedFolderName = "Synchronized";

    private static readonly MailFolderMapping FolderMapping = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("synchronized"),
        RemoteFolderPath.Create(SynchronizedFolderName, hierarchyDelimiter: '.'));

    private static readonly string[] SeededSubjects =
    [
        "synchronized-run-first",
        "synchronized-run-second",
        "synchronized-run-third",
    ];

    [Fact]
    [MailboxStateStep(1)]
    public async Task SynchronizeAsync_OverAFreshlySeededFolder_StoresEveryEmailAndLeavesEveryRemoteSeenFlagUnset()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        // Recreated rather than reused, so this class starts from a folder with no history whichever order the suite
        // has been run in before.
        await mailbox.RecreateFolderAsync(SynchronizedFolderName, cancellationToken);
        foreach (var subject in SeededSubjects)
        {
            await mailbox.AppendAsync(SynchronizedFolderName, subject, cancellationToken);
        }

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var result = await SynchronizeAsync(services, cancellationToken);

        // Assert
        Assert.Equal(MailboxSynchronizationOutcome.Synchronized, result.Outcome);
        Assert.Equal(SeededSubjects.Length, result.StoredEmailCount);
        Assert.False(result.HasMoreEmails);

        var storedSubjects = await ReadStoredSubjectsAsync(services, cancellationToken);
        Assert.Equal(SeededSubjects.Order(StringComparer.Ordinal), storedSubjects);

        RemoteSeenFlagAssertion.AssertNoneIsSeen(
            await mailbox.ReadAsync(SynchronizedFolderName, cancellationToken),
            "A full mailbox synchronization run");
    }

    [Fact]
    [MailboxStateStep(2)]
    public async Task SynchronizeAsync_OverAFolderAlreadySynchronized_StoresNothingTwiceAndKeepsTheCheckpoint()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var committedCheckpoint = await ReadCheckpointAsync(services, cancellationToken);

        // Act
        var result = await SynchronizeAsync(services, cancellationToken);

        // Assert
        Assert.Equal(MailboxSynchronizationOutcome.Synchronized, result.Outcome);
        Assert.Equal(0, result.StoredEmailCount);
        Assert.Equal(committedCheckpoint?.LastSeenUid, result.Checkpoint?.LastSeenUid);
        Assert.Equal(committedCheckpoint?.UidValidity, result.Checkpoint?.UidValidity);

        var storedSubjects = await ReadStoredSubjectsAsync(services, cancellationToken);
        Assert.Equal(SeededSubjects.Order(StringComparer.Ordinal), storedSubjects);

        RemoteSeenFlagAssertion.AssertNoneIsSeen(
            await mailbox.ReadAsync(SynchronizedFolderName, cancellationToken),
            "A repeated mailbox synchronization run");
    }

    /// <summary>Deletes and recreates the folder, which is how a real server hands out a new UIDVALIDITY.</summary>
    /// <remarks>
    /// The UIDs the previous incarnation handed out now name different mail, so the run must start the folder over
    /// rather than resume from a checkpoint that describes messages nobody can address any more. What it must not do is
    /// delete what it stored: those occurrences are a record of mail that existed, and the specification is explicit
    /// that a UIDVALIDITY change triggers controlled reconciliation rather than mass local deletion.
    /// </remarks>
    [Fact]
    [MailboxStateStep(3)]
    public async Task SynchronizeAsync_AfterTheFolderWasRecreatedWithANewUidValidity_StartsOverWithoutDeletingStoredEmails()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        var uidValidityBefore = await mailbox.ReadUidValidityAsync(SynchronizedFolderName, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var checkpointBefore = await ReadCheckpointAsync(services, cancellationToken);

        var uidValidityAfter = await mailbox.RecreateFolderAsync(SynchronizedFolderName, cancellationToken);
        const string subjectAfterRecreation = "synchronized-after-uidvalidity-change";
        await mailbox.AppendAsync(SynchronizedFolderName, subjectAfterRecreation, cancellationToken);

        // The arrangement is only an arrangement once the server actually handed out a different value, so this is
        // checked before the run rather than assumed by it.
        Assert.NotEqual(uidValidityBefore, uidValidityAfter);

        // Act
        var result = await SynchronizeAsync(services, cancellationToken);

        // Assert
        Assert.Equal(MailboxSynchronizationOutcome.Synchronized, result.Outcome);
        Assert.Equal(uidValidityAfter, result.Checkpoint?.UidValidity);
        Assert.NotEqual(checkpointBefore?.UidValidity, result.Checkpoint?.UidValidity);
        Assert.Equal(1, result.StoredEmailCount);

        var storedSubjects = await ReadStoredSubjectsAsync(services, cancellationToken);
        Assert.Equal(
            SeededSubjects.Append(subjectAfterRecreation).Order(StringComparer.Ordinal),
            storedSubjects);
    }

    private static Task<MailboxSynchronizationResult> SynchronizeAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxSynchronizer>().SynchronizeAsync(
                SyntheticMailAccount.AccountId,
                FolderMapping,
                token),
            cancellationToken);

    /// <summary>Reads back what the run actually persisted, rather than only what its result claims it did.</summary>
    private static Task<IReadOnlyList<string>> ReadStoredSubjectsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) => (IReadOnlyList<string>)await scope.GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(storedEmail => storedEmail.MailFolder.Alias == FolderMapping.Alias.Value)
                .Select(storedEmail => storedEmail.Subject!)
                .OrderBy(subject => subject)
                .ToArrayAsync(token),
            cancellationToken);

    private static Task<SynchronizationCheckpoint?> ReadCheckpointAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var resolutionStore = scope.GetRequiredService<IMailFolderResolutionStore>();
                var resolution = await resolutionStore.GetCurrentResolutionAsync(
                    SyntheticMailAccount.AccountId,
                    FolderMapping.Alias,
                    token);

                return resolution is null
                    ? null
                    : await scope.GetRequiredService<ISynchronizationCheckpointStore>().GetCheckpointAsync(
                        SyntheticMailAccount.AccountId,
                        resolution.Id,
                        token);
            },
            cancellationToken);
}
