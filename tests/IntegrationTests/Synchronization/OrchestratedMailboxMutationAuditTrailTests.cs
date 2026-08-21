// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>Proves the audit trail against real PostgreSQL and a real mail server together.</summary>
/// <remarks>
/// <para>
/// Two tests, because there are two claims no substitute can establish. The first is that a finished mutation of every
/// permitted kind leaves one entry that outlives the email it describes — which needs a real cascade to survive, since
/// what an in-memory double proves about a foreign key is only what it was written to prove. The second is that an
/// account with the trail off writes nothing at all, which is the default the privacy posture rests on and which the
/// same database has to show as an absence beside the presences above.
/// </para>
/// <para>
/// Everything else about the trail — what each entry states, what it deliberately omits, when retention erases one —
/// is a rule the unit suite exercises against substitutes and buys nothing here.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailboxMutationAuditTrailTests(MailFathomOrchestrationFixture orchestration)
{
    private const string ArchiveFolderName = "AuditTrailArchive";

    /// <summary>The alias this class owns, bound to the real inbox so a write session selects it.</summary>
    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("audit-trail-inbox"),
        RemoteFolderPath.Create(OrchestratedMailbox.InboxPath, hierarchyDelimiter: '.'));

    private static readonly RemoteFolderPath ArchivePath =
        RemoteFolderPath.Create(ArchiveFolderName, hierarchyDelimiter: '.');

    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Rule("keep-an-audit-trail", "1");

    /// <summary>The keywords the three keyword mutations are performed with, each on a message of its own.</summary>
    private static readonly AuthoredMailKeywords AuditedKeywords = AuthoredMailKeywords.Create(["AuditTrail"]);

    /// <summary>
    /// Every change MailFathom is permitted to make leaves exactly one entry on an account that asked for a trail, and
    /// every one of those entries is still there after the email it describes has been erased — including the entry for
    /// the deletion itself, which is the one an audit of deletions exists to hold.
    /// </summary>
    [Fact]
    public async Task AuditTrail_EveryMutationKind_KeepsOneEntryEachThatOutlivesTheEmail()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(ArchiveFolderName, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            auditTrailEnabled: true);

        await CommitInboxBindingAsync(services, cancellationToken);

        var requests = await ArrangeOneOfEachMutationAsync(services, mailbox, cancellationToken);

        // Act
        foreach (var request in requests)
        {
            await PerformAsync(services, request, cancellationToken);
        }

        var performedEntries = await ReadTrailAsync(services, cancellationToken);

        await EraseStoredEmailsAsync(services, requests, cancellationToken);

        // Assert
        var survivingEntries = await ReadTrailAsync(services, cancellationToken);

        Assert.Equal(
            MailboxMutation.All.Select(mutation => mutation.Name).Order(StringComparer.Ordinal),
            performedEntries
                .Where(entry => entry.Requester == Requester)
                .Select(entry => entry.Mutation.Name)
                .Order(StringComparer.Ordinal));

        Assert.All(
            performedEntries.Where(entry => entry.Requester == Requester),
            entry => Assert.Equal(MailboxMutationAuditOutcome.Performed, entry.Outcome));

        // The trail hangs on nothing the erasure reached, so the same entries are still readable afterwards. Ordered by
        // the identifier's own value rather than by the identity, which is a record struct and carries no comparison.
        Assert.Equal(
            performedEntries.Select(entry => entry.Id.Value).Order(),
            survivingEntries.Select(entry => entry.Id.Value).Order());

        // The source folder is the remote path rather than a key into a binding, which is what makes the entry
        // readable once the mail it describes is gone.
        Assert.All(
            survivingEntries.Where(entry => entry.Requester == Requester),
            entry => Assert.Equal(Inbox.RemotePath, entry.SourceFolderPath));
    }

    /// <summary>An account that never asked for a history accumulates none, which is what off by default has to mean.</summary>
    [Fact]
    public async Task AuditTrail_AccountWithTheTrailOff_KeepsNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await CommitInboxBindingAsync(services, cancellationToken);

        var subject = $"audit-trail-off-{Guid.NewGuid():N}";
        var occurrence = await mailbox.DeliverAndLocateAsync(Inbox.Id, subject, cancellationToken);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            occurrence,
            subject,
            cancellationToken);
        var unauditedRequester = MailboxMutationRequester.Command($"trail-off-{Guid.NewGuid():N}");
        var request = MailboxMutationRequest.SetSeen(storedEmailId, occurrence, unauditedRequester, isSeen: true);

        // Act
        var outcome = await PerformAsync(services, request, cancellationToken);

        // Assert
        Assert.Equal(MailboxMutationStatus.Performed, outcome.Status);

        var entries = await ReadTrailAsync(services, cancellationToken);
        Assert.DoesNotContain(entries, entry => entry.Requester == unauditedRequester);
    }

    /// <summary>Delivers one message per permitted mutation and builds the request that performs it.</summary>
    /// <remarks>
    /// Each mutation gets its own message, because a relocation and a delete both take their message out of the inbox
    /// and one request must not be aimed at an occurrence another has already moved.
    /// </remarks>
    private static async Task<IReadOnlyList<MailboxMutationRequest>> ArrangeOneOfEachMutationAsync(
        OrchestratedMailFathomServices services,
        OrchestratedMailbox mailbox,
        CancellationToken cancellationToken)
    {
        var requests = new List<MailboxMutationRequest>();

        foreach (var mutation in MailboxMutation.All)
        {
            var subject = $"audit-trail-{mutation.Name}-{Guid.NewGuid():N}";
            var occurrence = await mailbox.DeliverAndLocateAsync(Inbox.Id, subject, cancellationToken);
            var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
                services,
                occurrence,
                subject,
                cancellationToken);

            requests.Add(RequestFor(mutation, storedEmailId, occurrence));
        }

        return requests;
    }

    /// <summary>Builds the request that performs one permitted mutation, for every kind the domain publishes.</summary>
    /// <remarks>
    /// Every kind is named, and a kind this does not name throws rather than falling through to a request for some other
    /// kind. The claim above is that each published mutation leaves an entry of its own, so a fallthrough would perform
    /// one kind several times and report the missing kinds as an assertion about the trail — which is what a chain
    /// ending in <see cref="MailboxMutationRequest.SetSeen" /> did while the flag and keyword mutations were added.
    /// </remarks>
    private static MailboxMutationRequest RequestFor(
        MailboxMutation mutation,
        StoredEmailId storedEmailId,
        EmailOccurrenceId occurrence)
    {
        if (mutation == MailboxMutation.Relocate)
        {
            return MailboxMutationRequest.Relocate(storedEmailId, occurrence, Requester, ArchivePath);
        }

        if (mutation == MailboxMutation.Copy)
        {
            return MailboxMutationRequest.Copy(storedEmailId, occurrence, Requester, ArchivePath);
        }

        if (mutation == MailboxMutation.Delete)
        {
            return MailboxMutationRequest.Delete(
                storedEmailId,
                occurrence,
                Requester,
                AuthoredDeleteEmailDisposition.RetainLocalCopy);
        }

        if (mutation == MailboxMutation.SetSeen)
        {
            return MailboxMutationRequest.SetSeen(storedEmailId, occurrence, Requester, isSeen: true);
        }

        if (mutation == MailboxMutation.SetFlagged)
        {
            return MailboxMutationRequest.SetFlagged(storedEmailId, occurrence, Requester, isFlagged: true);
        }

        if (mutation == MailboxMutation.AddKeywords)
        {
            return MailboxMutationRequest.AddKeywords(storedEmailId, occurrence, Requester, AuditedKeywords);
        }

        if (mutation == MailboxMutation.RemoveKeywords)
        {
            return MailboxMutationRequest.RemoveKeywords(storedEmailId, occurrence, Requester, AuditedKeywords);
        }

        if (mutation == MailboxMutation.SetKeywords)
        {
            return MailboxMutationRequest.SetKeywords(storedEmailId, occurrence, Requester, AuditedKeywords);
        }

        throw new NotSupportedException(
            $"The audit trail suite performs every published mutation, and it names no request for '{mutation.Name}'.");
    }

    private static Task<MailboxMutationOutcome> PerformAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationPerformer>().PerformAsync(
                request,
                Inbox,
                scope.GetRequiredService<IMailTransportSecurityPolicyReader>()
                    .GetPolicy(SyntheticMailAccount.AccountId),
                token),
            cancellationToken);

    /// <summary>Reads the whole of this account's trail through the port an operator's page is served from.</summary>
    private static Task<IReadOnlyList<MailboxMutationAuditEntry>> ReadTrailAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var queryResult = MailboxMutationAuditQuery.Create(
                    SyntheticMailAccount.AccountId,
                    mutation: default,
                    completedFrom: null,
                    completedBefore: null,
                    MailboxMutationAuditQuery.MaximumPageSize,
                    cursor: null);

                var page = await scope.GetRequiredService<IMailboxMutationAuditEntryStore>()
                    .ReadPageAsync(queryResult.Query!, token);

                return page.Entries;
            },
            cancellationToken);

    /// <summary>Erases the stored emails the entries describe, which is the cascade the trail has to survive.</summary>
    /// <remarks>
    /// Deleted through the context rather than through a disposition, because what is under test is the schema: the
    /// mutation records cascade from the email by design and the audit entries must not, and a set-based delete is the
    /// bluntest form of that question.
    /// </remarks>
    private static Task<int> EraseStoredEmailsAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyList<MailboxMutationRequest> requests,
        CancellationToken cancellationToken)
    {
        var erasedIds = requests.Select(request => request.StoredEmailId.Value).ToArray();

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .Where(email => erasedIds.Contains(email.Id))
                .ExecuteDeleteAsync(token),
            cancellationToken);
    }

    private static async Task CommitInboxBindingAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IMailFolderResolutionStore>().SaveResolutionAsync(
                    session,
                    SyntheticMailAccount.AccountId,
                    Inbox,
                    token),
                cancellationToken));
}
