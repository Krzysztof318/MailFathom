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
/// One test, and it carries both messages, because the whole claim is a comparison: the server reports the identical
/// moved flag whichever side wrote it, and the only thing separating them is a record MailFathom wrote before the
/// <c>STORE</c> went out. Two tests would pay twice for one composition and could not observe both halves of the same
/// run.
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

    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Rule("mark-newsletters-read", 1);

    /// <summary>The flag MailFathom set is withheld from rule evaluation, and the one the owner set beside it is not.</summary>
    [Fact]
    public async Task SynchronizeAsync_AfterTwoSeenFlagsOneSideEach_WithholdsOnlyTheOneMailFathomSet()
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

        var outcome = await MarkSeenAsync(services, ours, cancellationToken);
        Assert.Equal(MailboxMutationStatus.Performed, outcome.Status);

        await mailbox.MarkSeenAsync(FolderName, theirs.Uid, cancellationToken);

        // Act
        var result = await SynchronizeAsync(services, cancellationToken);

        // Assert
        Assert.Equal(1, result.Reconciliation.SeenStateChangedEmailCount);

        var suppressed = Assert.Single(result.SuppressedChanges);
        Assert.Equal(MailboxChangeKind.SeenStateChanged, suppressed.Kind);
        Assert.Equal(MailboxMutation.SetSeen, suppressed.Mutation);
        Assert.Equal(ours.StoredEmailId, suppressed.StoredEmailId);
        Assert.Equal(outcome.RecordId, suppressed.MutationRecordId);

        // The stored snapshot follows the server for both, because what was withheld is the trigger and never the
        // reading. An assertion that one flag was withheld says nothing unless the other one really did move.
        Assert.True(await IsRemotelySeenAsync(services, markedByMailFathom, cancellationToken));
        Assert.True(await IsRemotelySeenAsync(services, markedByOwner, cancellationToken));
    }

    private static Task<MailboxSynchronizationResult> SynchronizeAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxSynchronizer>().SynchronizeAsync(
                SyntheticMailAccount.AccountId,
                Mapping,
                token),
            cancellationToken);

    /// <summary>Marks one stored email read through the production performer, which writes the record the join reads.</summary>
    private static Task<MailboxMutationOutcome> MarkSeenAsync(
        OrchestratedMailFathomServices services,
        StoredEmailRow stored,
        CancellationToken cancellationToken)
    {
        var folder = MailFolderResolution.FirstBindingOf(Mapping.Alias, Mapping.RemotePath!.Value);
        var occurrence = EmailOccurrenceId.Create(
            SyntheticMailAccount.AccountId,
            folder.Id,
            stored.UidValidity,
            stored.Uid);
        var request = MailboxMutationRequest.SetSeen(stored.StoredEmailId, occurrence, Requester, isSeen: true);

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
