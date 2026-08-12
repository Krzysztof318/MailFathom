// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
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

/// <summary>Proves a delete MailFathom performed against a real server, and what each disposition leaves behind.</summary>
/// <remarks>
/// <para>
/// The whole class runs against an account configured to erase what its server loses, which is the arrangement that
/// makes the point: every message below disappears from its folder, and not one of them is disposed of by that setting.
/// Reading the account's setting where the deletion completes would destroy all three rows, so the two that survive are
/// what says the record decided instead.
/// </para>
/// <para>
/// One run over three messages rather than three runs over one. The disposition is applied where a reconciliation
/// window is committed, so a window carrying all three proves the three outcomes come out of the same transaction and
/// costs one composition instead of three. It is also the control the suite's rules ask for: the erased row's absence
/// would prove nothing if the same window could not report a row present, and it reports two.
/// </para>
/// <para>
/// GreenMail advertises <c>UIDPLUS</c>, so the message-scoped <c>UID EXPUNGE</c> the delete rests on is really issued
/// rather than substituted. What that command does to the folder, and that a neighbour another client flagged is
/// spared, belongs to the write session's own tests; what this class adds is everything downstream of the server's
/// acknowledgement.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedAuthoredDeleteTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderName = "AuthoredDelete";

    private static readonly MailFolderMapping FolderMapping = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("authored-delete"),
        RemoteFolderPath.Create(FolderName, hierarchyDelimiter: '.'));

    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Rule("free-the-server", "1");

    /// <summary>Each disposition decides one local copy, and the account's remote-deletion setting decides none of them.</summary>
    [Fact]
    public async Task SynchronizeAsync_AfterDeletesTheOwnerAuthored_DisposesOfEachLocalCopyAsItsOwnRecordSaid()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(FolderName, cancellationToken);

        var run = Guid.NewGuid().ToString("N");
        var subjects = new Dictionary<AuthoredDeleteEmailDisposition, string>
        {
            [AuthoredDeleteEmailDisposition.RetainLocalCopy] = $"authored-delete-retained-{run}",
            [AuthoredDeleteEmailDisposition.RetainTombstone] = $"authored-delete-tombstoned-{run}",
            [AuthoredDeleteEmailDisposition.EraseLocalCopy] = $"authored-delete-erased-{run}",
        };

        foreach (var subject in subjects.Values)
        {
            await mailbox.AppendAsync(FolderName, subject, cancellationToken);
        }

        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            RemotelyDeletedEmailDisposition.EraseLocalCopy);
        Assert.Equal(
            subjects.Count,
            (await SynchronizeAsync(services, cancellationToken)).StoredEmailCount);

        var storedIds = new Dictionary<AuthoredDeleteEmailDisposition, StoredEmailId>();

        foreach (var (disposition, subject) in subjects)
        {
            var stored = await ReadStoredEmailAsync(services, subject, cancellationToken);
            storedIds[disposition] = stored.StoredEmailId;

            var outcome = await DeleteAsync(services, stored, disposition, cancellationToken);
            Assert.Equal(MailboxMutationStatus.Performed, outcome.Status);
        }

        // Act
        var result = await SynchronizeAsync(services, cancellationToken);

        // Assert
        Assert.Equal(subjects.Count, result.Reconciliation.OwnMutationCompletedEmailCount);
        Assert.Equal(0, result.Reconciliation.RemotelyDeletedEmailCount);
        Assert.Empty(await mailbox.ReadAsync(FolderName, cancellationToken));

        // The mail the owner asked to free space for is still theirs to read, which is the outcome that separates
        // deleting on the server from forgetting the mail.
        var retained = await ReadStoredEmailAsync(
            services,
            subjects[AuthoredDeleteEmailDisposition.RetainLocalCopy],
            cancellationToken);
        Assert.True(retained.IsRetainedAfterAuthoredDelete);
        Assert.NotNull(retained.RemoteExpungeObservedAt);
        Assert.NotNull(await ReadSummaryAsync(services, retained.StoredEmailId, cancellationToken));

        // The tombstone keeps the record that the email existed and takes the mail out of every query, exactly as the
        // counterpart setting does for a disappearance somebody else caused.
        var tombstoned = await ReadStoredEmailAsync(
            services,
            subjects[AuthoredDeleteEmailDisposition.RetainTombstone],
            cancellationToken);
        Assert.False(tombstoned.IsRetainedAfterAuthoredDelete);
        Assert.NotNull(tombstoned.RemoteExpungeObservedAt);
        Assert.Null(await ReadSummaryAsync(services, tombstoned.StoredEmailId, cancellationToken));

        // Nothing of the third survives, and PostgreSQL took the raw MIME with it through the cascade the row owns.
        Assert.Equal(
            0,
            await CountStoredEmailsAsync(
                services,
                subjects[AuthoredDeleteEmailDisposition.EraseLocalCopy],
                cancellationToken));
        Assert.Equal(
            0,
            await CountStoredContentsAsync(
                services,
                storedIds[AuthoredDeleteEmailDisposition.EraseLocalCopy],
                cancellationToken));

        // Every disappearance was MailFathom's own, so none of them reaches rule evaluation as a change to react to.
        Assert.Equal(subjects.Count, result.SuppressedChanges.Count);
        Assert.All(result.SuppressedChanges, suppressed =>
        {
            Assert.Equal(MailboxChangeKind.EmailLeftFolder, suppressed.Kind);
            Assert.Equal(MailboxMutation.Delete, suppressed.Mutation);
        });
    }

    private static Task<MailboxSynchronizationResult> SynchronizeAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxSynchronizer>().SynchronizeAsync(
                SyntheticMailAccount.AccountId,
                FolderMapping,
                token),
            cancellationToken);

    /// <summary>Deletes one stored email through the production performer, which writes the record the window reads.</summary>
    private static Task<MailboxMutationOutcome> DeleteAsync(
        OrchestratedMailFathomServices services,
        StoredEmailRow stored,
        AuthoredDeleteEmailDisposition localDisposition,
        CancellationToken cancellationToken)
    {
        var folder = MailFolderResolution.FirstBindingOf(FolderMapping.Alias, FolderMapping.RemotePath!.Value);
        var occurrence = EmailOccurrenceId.Create(
            SyntheticMailAccount.AccountId,
            folder.Id,
            stored.UidValidity,
            stored.Uid);
        var request = MailboxMutationRequest.Delete(
            stored.StoredEmailId,
            occurrence,
            Requester,
            localDisposition);

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
                    ImapUid.Create(storedEmail.Uid),
                    storedEmail.RemoteExpungeObservedAt,
                    storedEmail.IsRetainedAfterAuthoredDelete))
                .SingleAsync(token),
            cancellationToken);

    /// <summary>Asks the mailbox read path what it serves, which is what a tombstone takes an email out of.</summary>
    private static Task<EmailSummary?> ReadSummaryAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailSummaryReader>().FindAsync(storedEmailId, token),
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

    private static Task<int> CountStoredContentsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .EmailMessageContents
                .AsNoTracking()
                .CountAsync(content => content.StoredEmailId == storedEmailId.Value, token),
            cancellationToken);

    /// <summary>The columns of one stored email this class reads back.</summary>
    private sealed record StoredEmailRow(
        StoredEmailId StoredEmailId,
        ImapUidValidity UidValidity,
        ImapUid Uid,
        DateTimeOffset? RemoteExpungeObservedAt,
        bool IsRetainedAfterAuthoredDelete);
}
