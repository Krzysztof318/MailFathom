// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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

/// <summary>Proves that a <c>\Seen</c> flag MailFathom set is told from one the mailbox owner set, against a real server and a real database.</summary>
/// <remarks>
/// <para>
/// One test, and it carries both messages and both directions, because the whole claim is a comparison: the server
/// reports the identical moved flag whichever side wrote it and whichever way it moved, and the only thing separating
/// them is a record MailFathom wrote before the <c>STORE</c> went out. Splitting it would pay several times for one
/// composition and could not observe the halves of the same run against each other.
/// </para>
/// <para>
/// What only real infrastructure establishes here is the query. Whether a change is withheld is decided in the domain
/// and covered by unit tests; whether the read that finds the candidate records translates, filters by the folder
/// binding it claims to, and returns the row is a statement about PostgreSQL and EF Core that no substitute can make.
/// It is the one read of that store nothing else in this suite reaches.
/// </para>
/// <para>
/// The suppression does not rest on <c>CONDSTORE</c>, which is what makes this provable against a server advertising
/// neither it nor <c>QRESYNC</c>. A moved flag is the value the server reports differing from the value last stored, and
/// the ordinary window reads both. What <c>CONDSTORE</c> would change is how much of the folder the server has to
/// describe, never how a change is attributed.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSeenStateProvenanceTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderName = "SeenStateProvenance";

    private static readonly MailFolderMapping Mapping = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("seen-state-provenance"),
        RemoteFolderPath.Create(FolderName, hierarchyDelimiter: '.'));

    private static readonly MailboxMutationRequester MarkingRule =
        MailboxMutationRequester.Rule("mark-newsletters-read", 1);

    /// <summary>
    /// A second rule, because the idempotency identity is the occurrence, the mutation, and who asked, and a
    /// <c>\Seen</c> change is the one mutation that leaves the occurrence where it was. The same rule asking again about
    /// the same occurrence is therefore answered from its own record and issues nothing, which is what stops a rule
    /// fighting an owner who reverted its change by hand. Putting the two directions on one requester would test that
    /// answer rather than the clearing this class is about.
    /// </summary>
    private static readonly MailboxMutationRequester SurfacingRule =
        MailboxMutationRequester.Rule("surface-unpaid-invoices", 1);

    /// <summary>Each flag MailFathom moved is withheld from rule evaluation, the owner's is not, and the stored value follows only an observation.</summary>
    [Fact]
    public async Task SynchronizeAsync_AfterSeenFlagsMovedByBothSidesInBothDirections_WithholdsOnlyItsOwnAndMirrorsOnlyWhatItObserved()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(FolderName, cancellationToken);

        var markedByMailFathom = $"seen-by-mailfathom-{Guid.NewGuid():N}";
        var markedByOwner = $"seen-by-owner-{Guid.NewGuid():N}";
        await mailbox.AppendAsync(FolderName, markedByMailFathom, cancellationToken);
        await mailbox.AppendAsync(FolderName, markedByOwner, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // The first run stores both and reads their flags, which is what gives the second run a previous value to
        // compare against. Without it neither message would have a moved flag at all.
        var firstRun = await SynchronizeAsync(services, cancellationToken);
        Assert.Equal(2, firstRun.StoredEmailCount);
        Assert.Equal(2, firstRun.Reconciliation.ObservedEmailCount);

        var ours = await ReadStoredEmailAsync(services, markedByMailFathom, cancellationToken);
        var theirs = await ReadStoredEmailAsync(services, markedByOwner, cancellationToken);

        // Act
        //
        // Every stored reading is captured where it means something rather than at the end, because the column under
        // test is the one that changes as the sequence proceeds: read after the run instead of before it, an assertion
        // that the value lagged the command would be indistinguishable from one that it never followed the server.
        var setOutcome = await ChangeSeenStateAsync(services, ours, MarkingRule, isSeen: true, cancellationToken);
        var storedAfterTheSetCommand = await IsRemotelySeenAsync(services, markedByMailFathom, cancellationToken);

        await mailbox.MarkSeenAsync(FolderName, theirs.Uid, cancellationToken);

        var afterSetting = await SynchronizeAsync(services, cancellationToken);
        var oursAfterSettingRun = await IsRemotelySeenAsync(services, markedByMailFathom, cancellationToken);
        var theirsAfterSettingRun = await IsRemotelySeenAsync(services, markedByOwner, cancellationToken);

        var clearOutcome = await ChangeSeenStateAsync(services, ours, SurfacingRule, isSeen: false, cancellationToken);
        var storedAfterTheClearCommand = await IsRemotelySeenAsync(services, markedByMailFathom, cancellationToken);

        var afterClearing = await SynchronizeAsync(services, cancellationToken);
        var oursAfterClearingRun = await IsRemotelySeenAsync(services, markedByMailFathom, cancellationToken);
        var theirsAfterClearingRun = await IsRemotelySeenAsync(services, markedByOwner, cancellationToken);

        // Assert
        Assert.Equal(MailboxMutationStatus.Performed, setOutcome.Status);
        Assert.Equal(MailboxMutationStatus.Performed, clearOutcome.Status);

        // The server has served each command and no run has read the folder in between, so the stored value must still
        // be the one the last window saw. This is the whole of what keeps the column a mirror: neither request wrote a
        // local flag, in either direction.
        Assert.False(storedAfterTheSetCommand);
        Assert.True(storedAfterTheClearCommand);

        Assert.Equal(1, afterSetting.Reconciliation.SeenStateChangedEmailCount);
        AssertWithheld(afterSetting, ours.StoredEmailId, setOutcome.RecordId);

        Assert.Equal(0, afterClearing.Reconciliation.SeenStateChangedEmailCount);
        AssertWithheld(afterClearing, ours.StoredEmailId, clearOutcome.RecordId);

        // The stored snapshot follows the server for every flag that moved, because what was withheld is the trigger
        // and never the reading. An assertion that one change was withheld says nothing unless the owner's own change
        // beside it really did arrive, and unless the withheld one really did reach the column.
        Assert.True(oursAfterSettingRun);
        Assert.True(theirsAfterSettingRun);
        Assert.False(oursAfterClearingRun);
        Assert.True(theirsAfterClearingRun);
    }

    /// <summary>Requires a run to have withheld exactly the one flag change the named record accounts for.</summary>
    private static void AssertWithheld(
        MailboxSynchronizationResult result,
        StoredEmailId storedEmailId,
        MailboxMutationRecordId recordId)
    {
        var suppressed = Assert.Single(result.SuppressedChanges);

        Assert.Equal(MailboxChangeKind.SeenStateChanged, suppressed.Kind);
        Assert.Equal(MailboxMutation.SetSeen, suppressed.Mutation);
        Assert.Equal(storedEmailId, suppressed.StoredEmailId);
        Assert.Equal(recordId, suppressed.MutationRecordId);
    }

    private static Task<MailboxSynchronizationResult> SynchronizeAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxSynchronizer>().SynchronizeAsync(
                SyntheticMailAccount.AccountId,
                Mapping,
                token),
            cancellationToken);

    /// <summary>Moves one stored email's remote flag through the production performer, which writes the record the join reads.</summary>
    private static Task<MailboxMutationOutcome> ChangeSeenStateAsync(
        OrchestratedMailFathomServices services,
        StoredEmailRow stored,
        MailboxMutationRequester requester,
        bool isSeen,
        CancellationToken cancellationToken)
    {
        var folder = MailFolderResolution.FirstBindingOf(Mapping.Alias, Mapping.RemotePath!.Value);
        var occurrence = EmailOccurrenceId.Create(
            SyntheticMailAccount.AccountId,
            folder.Id,
            stored.UidValidity,
            stored.Uid);
        var request = MailboxMutationRequest.SetSeen(stored.StoredEmailId, occurrence, requester, isSeen);

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationPerformer>().PerformAsync(
                request,
                folder,
                scope.GetRequiredService<IMailTransportSecurityPolicyReader>()
                    .GetPolicy(SyntheticMailAccount.AccountId),
                token),
            cancellationToken);
    }

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
                    ImapUidValidity.Create(storedEmail.UidValidity),
                    ImapUid.Create(storedEmail.Uid)))
                .SingleAsync(token),
            cancellationToken);

    private static Task<bool> IsRemotelySeenAsync(
        OrchestratedMailFathomServices services,
        string subject,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) => await scope.GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(storedEmail => storedEmail.Subject == subject)
                .Select(storedEmail => storedEmail.IsRemotelySeen)
                .SingleAsync(token),
            cancellationToken);

    /// <summary>The columns of one stored email this class reads back.</summary>
    private sealed record StoredEmailRow(
        StoredEmailId StoredEmailId,
        ImapUidValidity UidValidity,
        ImapUid Uid);
}
