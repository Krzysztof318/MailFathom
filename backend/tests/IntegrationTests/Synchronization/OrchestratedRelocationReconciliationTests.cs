// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>Proves that a message MailFathom moved stays one local email, against a real server and a real database.</summary>
/// <remarks>
/// <para>
/// Neither substitute can establish this. The join is made from the <c>COPYUID</c> a real server chose to send, against
/// a UID that server assigned, and what it has to produce is one row where a unique index over folder, UIDVALIDITY, and
/// UID would otherwise hold two. GreenMail advertises <c>UIDPLUS</c>, so the response the join rests on is really there.
/// </para>
/// <para>
/// Two tests, because the two halves cannot both be observed in one order. Carrying the row into the destination folder
/// takes it out of the source folder locally, so the source disappearance is only observable when the source folder is
/// reconciled first — which is the second test, and also the out-of-order case an operator's mailbox will produce
/// whenever the destination folder is not one MailFathom synchronizes on the same schedule.
/// </para>
/// <para>
/// Both also assert what each half was withheld from, and the third test is the control that makes those assertions
/// mean anything: the mailbox owner performs the same move by hand, over a connection MailFathom never sees, and the
/// same two events are raised rather than withheld. An absence proves nothing unless the same observation would report
/// it present, and the two tests differ in exactly one thing — whether a record was written before the command.
/// </para>
/// <para>
/// The same mechanism applied to a <c>\Seen</c> flag is proved by <c>OrchestratedSeenStateProvenanceTests</c>, which
/// this class deliberately leaves alone: nothing about a flag is a folder change, and neither run here would observe it.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
[TestCaseOrderer(typeof(MailboxStateSequenceOrderer))]
public sealed class OrchestratedRelocationReconciliationTests(MailFathomOrchestrationFixture orchestration)
{
    private const string SourceFolderName = "RelocationSource";

    private const string TargetFolderName = "RelocationTarget";

    private const string ManualSourceFolderName = "ManualMoveSource";

    private const string ManualTargetFolderName = "ManualMoveTarget";

    private static readonly MailFolderMapping SourceMapping = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("relocation-source"),
        RemoteFolderPath.Create(SourceFolderName, hierarchyDelimiter: '.'));

    private static readonly MailFolderMapping TargetMapping = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("relocation-target"),
        RemoteFolderPath.Create(TargetFolderName, hierarchyDelimiter: '.'));

    private static readonly MailFolderMapping ManualSourceMapping = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("manual-move-source"),
        RemoteFolderPath.Create(ManualSourceFolderName, hierarchyDelimiter: '.'));

    private static readonly MailFolderMapping ManualTargetMapping = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("manual-move-target"),
        RemoteFolderPath.Create(ManualTargetFolderName, hierarchyDelimiter: '.'));

    private static readonly RemoteFolderPath TargetPath =
        RemoteFolderPath.Create(TargetFolderName, hierarchyDelimiter: '.');

    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Rule("file-to-relocation-target", "1");

    /// <summary>The message arrives in its new folder as an ordinary discovery, and is the email that was already stored.</summary>
    [Fact]
    [MailboxStateStep(1)]
    public async Task SynchronizeAsync_AfterARelocationIntoTheFolder_CarriesTheLocalEmailAcrossInsteadOfStoringASecond()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(SourceFolderName, cancellationToken);
        await mailbox.RecreateFolderAsync(TargetFolderName, cancellationToken);

        var subject = $"relocation-carried-{Guid.NewGuid():N}";
        await mailbox.AppendAsync(SourceFolderName, subject, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        Assert.Equal(1, (await SynchronizeAsync(services, SourceMapping, cancellationToken)).StoredEmailCount);

        var stored = await ReadStoredEmailAsync(services, subject, cancellationToken);
        var outcome = await RelocateToTargetAsync(services, stored, cancellationToken);

        // The arrangement only proves what this test is about once the server really named the destination occurrence,
        // so the response the join rests on is asserted before the run rather than assumed by it.
        Assert.Equal(MailboxMutationStatus.Performed, outcome.Status);
        Assert.True(outcome.Placement.IsReported);

        // Act
        var result = await SynchronizeAsync(services, TargetMapping, cancellationToken);

        // Assert
        Assert.Equal(1, result.RelocatedEmailCount);
        Assert.Equal(0, result.StoredEmailCount);

        var carried = await ReadStoredEmailAsync(services, subject, cancellationToken);
        Assert.Equal(stored.StoredEmailId, carried.StoredEmailId);
        Assert.Equal(TargetMapping.Alias.Value, carried.Alias);
        Assert.Equal(outcome.Placement.Uid, carried.Uid);

        // One row, where a run without the join would have left the source occurrence beside the destination one.
        Assert.Equal(1, await CountStoredEmailsAsync(services, subject, cancellationToken));

        var record = await ReadRecordAsync(services, outcome.RecordId, cancellationToken);
        Assert.NotNull(record.PlacementObservedAt);
        Assert.NotNull(record.SourceRemovalObservedAt);

        // The arrival was MailFathom's own, so it is withheld from rule evaluation. Raising it is what would let a rule
        // that files mail match the mail it has just filed, on every interval, for as long as the folder is watched.
        var suppressed = Assert.Single(result.SuppressedChanges);
        Assert.Equal(MailboxChangeKind.EmailAppearedInFolder, suppressed.Kind);
        Assert.Equal(MailboxMutation.Relocate, suppressed.Mutation);
        Assert.Equal(outcome.RecordId, suppressed.MutationRecordId);
    }

    /// <summary>The source occurrence vanishing is the relocation completing, not a deletion somebody else made.</summary>
    [Fact]
    [MailboxStateStep(2)]
    public async Task SynchronizeAsync_AfterARelocationOutOfTheFolder_AttributesTheDisappearanceToTheMutationRatherThanToTheServer()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        var subject = $"relocation-attributed-{Guid.NewGuid():N}";
        await mailbox.AppendAsync(SourceFolderName, subject, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        Assert.Equal(1, (await SynchronizeAsync(services, SourceMapping, cancellationToken)).StoredEmailCount);

        var stored = await ReadStoredEmailAsync(services, subject, cancellationToken);
        var outcome = await RelocateToTargetAsync(services, stored, cancellationToken);
        Assert.Equal(MailboxMutationStatus.Performed, outcome.Status);

        // Act
        var result = await SynchronizeAsync(services, SourceMapping, cancellationToken);

        // Assert
        Assert.Equal(0, result.Reconciliation.RemotelyDeletedEmailCount);
        Assert.Equal(1, result.Reconciliation.OwnMutationCompletedEmailCount);

        // The row stays where it is, waiting for the destination folder to be synchronized. A remote deletion would
        // have tombstoned it out of every mailbox query instead.
        var afterReconciliation = await ReadStoredEmailAsync(services, subject, cancellationToken);
        Assert.Equal(stored.StoredEmailId, afterReconciliation.StoredEmailId);
        Assert.Null(afterReconciliation.RemoteExpungeObservedAt);

        var record = await ReadRecordAsync(services, outcome.RecordId, cancellationToken);
        Assert.NotNull(record.SourceRemovalObservedAt);
        Assert.Null(record.PlacementObservedAt);

        var suppressed = Assert.Single(result.SuppressedChanges);
        Assert.Equal(MailboxChangeKind.EmailLeftFolder, suppressed.Kind);
        Assert.Equal(MailboxMutation.Relocate, suppressed.Mutation);
        Assert.Equal(stored.StoredEmailId, suppressed.StoredEmailId);
        Assert.Equal(outcome.RecordId, suppressed.MutationRecordId);
    }

    /// <summary>The mailbox owner moving mail by hand produces the same two events, and both stay changes to react to.</summary>
    /// <remarks>
    /// <para>
    /// This is the control for the two tests above. The server reports the identical arrival and the identical
    /// disappearance whichever side issued the command, so an assertion that MailFathom's own were withheld says nothing
    /// until the same runs raise the owner's. It also proves the suppression is scoped to the occurrence a record names
    /// rather than to the folders a rule happens to write to.
    /// </para>
    /// <para>
    /// It owns a folder pair of its own rather than reusing the one above, because the tests above deliberately leave a
    /// relocation half-observed: the message the second test moved is still waiting to be met in the target folder, and
    /// its source occurrence is still recorded as MailFathom's. Reusing those folders would let this test's runs meet
    /// that message and report a withheld change this test never made.
    /// </para>
    /// </remarks>
    [Fact]
    [MailboxStateStep(3)]
    public async Task SynchronizeAsync_AfterAFolderChangeMadeOutsideMailFathom_RaisesBothHalvesOfIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(ManualSourceFolderName, cancellationToken);
        await mailbox.RecreateFolderAsync(ManualTargetFolderName, cancellationToken);

        var subject = $"relocation-by-hand-{Guid.NewGuid():N}";
        await mailbox.AppendAsync(ManualSourceFolderName, subject, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        Assert.Equal(1, (await SynchronizeAsync(services, ManualSourceMapping, cancellationToken)).StoredEmailCount);

        var stored = await ReadStoredEmailAsync(services, subject, cancellationToken);
        await mailbox.MoveAsync(ManualSourceFolderName, ManualTargetFolderName, stored.Uid, cancellationToken);

        // Act
        var sourceRun = await SynchronizeAsync(services, ManualSourceMapping, cancellationToken);
        var targetRun = await SynchronizeAsync(services, ManualTargetMapping, cancellationToken);

        // Assert
        Assert.Empty(sourceRun.SuppressedChanges);
        Assert.Equal(1, sourceRun.Reconciliation.RemotelyDeletedEmailCount);
        Assert.Equal(0, sourceRun.Reconciliation.OwnMutationCompletedEmailCount);

        Assert.Empty(targetRun.SuppressedChanges);
        Assert.Equal(1, targetRun.StoredEmailCount);
        Assert.Equal(0, targetRun.RelocatedEmailCount);
    }

    private static Task<MailboxSynchronizationResult> SynchronizeAsync(
        OrchestratedMailFathomServices services,
        MailFolderMapping mapping,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxSynchronizer>().SynchronizeAsync(
                SyntheticMailAccount.Account,
                mapping,
                token),
            cancellationToken);

    /// <summary>Relocates one stored email through the production performer, which writes the record the join reads.</summary>
    private static Task<MailboxMutationOutcome> RelocateToTargetAsync(
        OrchestratedMailFathomServices services,
        StoredEmailRow stored,
        CancellationToken cancellationToken)
    {
        var folder = MailFolderResolution.FirstBindingOf(SourceMapping.Alias, SourceMapping.RemotePath!.Value);
        var occurrence = EmailOccurrenceId.Create(
            SyntheticMailAccount.AccountId,
            folder.Id,
            stored.UidValidity,
            stored.Uid);
        var request = MailboxMutationRequest.Relocate(stored.StoredEmailId, SyntheticMailAccount.Owner, occurrence, Requester, TargetPath);

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationPerformer>().PerformAsync(
                request,
                folder,
                scope.GetRequiredService<IMailTransportSecurityPolicyReader>()
                    .GetPolicy(SyntheticMailAccount.AccountId),
                token),
            cancellationToken);
    }

    /// <summary>Reads the one row stored for a subject, whichever folder binding currently carries it.</summary>
    private static Task<StoredEmailRow> ReadStoredEmailAsync(
        OrchestratedMailFathomServices services,
        string subject,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) => await scope.GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(storedEmail => storedEmail.Subject == subject)
                .Select(storedEmail => new StoredEmailRow(
                    StoredEmailId.Create(storedEmail.Id),
                    storedEmail.MailFolder.Alias,
                    ImapUidValidity.Create(storedEmail.UidValidity),
                    ImapUid.Create(storedEmail.Uid),
                    storedEmail.RemoteExpungeObservedAt))
                .SingleAsync(token),
            cancellationToken);

    private static Task<int> CountStoredEmailsAsync(
        OrchestratedMailFathomServices services,
        string subject,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .CountAsync(storedEmail => storedEmail.Subject == subject, token),
            cancellationToken);

    /// <summary>Reads the two observations off the record's own row, because a completed mutation is no longer outstanding.</summary>
    private static Task<MutationObservationRow> ReadRecordAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) => await scope.GetRequiredService<MailFathomDbContext>()
                .MailboxMutations
                .AsNoTracking()
                .Where(mutation => mutation.Id == recordId.Value)
                .Select(mutation => new MutationObservationRow(
                    mutation.PlacementObservedAt,
                    mutation.SourceRemovalObservedAt))
                .SingleAsync(token),
            cancellationToken);

    /// <summary>The columns of one stored email this class reads back.</summary>
    private sealed record StoredEmailRow(
        StoredEmailId StoredEmailId,
        string Alias,
        ImapUidValidity UidValidity,
        ImapUid Uid,
        DateTimeOffset? RemoteExpungeObservedAt);

    /// <summary>What one mutation record says synchronization has accounted for.</summary>
    private sealed record MutationObservationRow(
        DateTimeOffset? PlacementObservedAt,
        DateTimeOffset? SourceRemovalObservedAt);
}
