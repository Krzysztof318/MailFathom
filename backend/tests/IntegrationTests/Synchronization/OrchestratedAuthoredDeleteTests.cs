// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.BrowseSearch;
using MailFathom.Application.Emails.BrowseThread;
using MailFathom.Application.Emails.BrowseTimeline;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.SyntheticMail.Generation;
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
/// <para>
/// The second test is the far end of that: what a caller is served afterwards. Every read the client surface publishes
/// resolves what it may return through one shared expression, and a claim made about that expression is a claim about
/// a line of code rather than about four answers — so the timeline, the conversation, the search, and the message read
/// are each asked in their own right, in a folder of their own so what they answer with is exactly this arrangement.
/// Its messages carry an identifier derived from their subject, which puts each of the three in a conversation of its
/// own and makes the conversation read answerable per disposition like the other three.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedAuthoredDeleteTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderName = "AuthoredDelete";

    private const string ReadFolderName = "AuthoredDeleteReads";

    private static readonly MailFolderMapping FolderMapping = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("authored-delete"),
        RemoteFolderPath.Create(FolderName, hierarchyDelimiter: '.'));

    /// <summary>The folder the read test owns, so a listing narrowed to it answers with that test's three messages and nothing else.</summary>
    private static readonly MailFolderMapping ReadFolderMapping = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create("authored-delete-reads"),
        RemoteFolderPath.Create(ReadFolderName, hierarchyDelimiter: '.'));

    /// <summary>Who the read test's messages are from, which is what gives each of them an identifier and a conversation of its own.</summary>
    private static readonly SyntheticParticipant ReadAuthor =
        new("Zofia Kowalska", "Zofia.Kowalska@authored-delete.test");

    private static readonly MailAccountSelector ReadAccount =
        MailAccountSelector.For(SyntheticMailAccount.AccountId);

    private static readonly MailFolderReference ReadFolder = MailFolderReference.ToAlias(ReadFolderMapping.Alias);

    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Rule("free-the-server", "1");

    /// <summary>Each disposition decides one local copy, and the account's remote-deletion setting decides none of them.</summary>
    [Fact]
    public async Task SynchronizeAsync_AfterDeletesTheUserAuthored_DisposesOfEachLocalCopyAsItsOwnRecordSaid()
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
            (await SynchronizeAsync(services, FolderMapping, cancellationToken)).StoredEmailCount);

        var storedIds = new Dictionary<AuthoredDeleteEmailDisposition, StoredEmailId>();

        foreach (var (disposition, subject) in subjects)
        {
            var stored = await ReadStoredEmailAsync(services, subject, cancellationToken);
            storedIds[disposition] = stored.StoredEmailId;

            var outcome = await DeleteAsync(services, FolderMapping, stored, disposition, cancellationToken);
            Assert.Equal(MailboxMutationStatus.Performed, outcome.Status);
        }

        // Act
        var result = await SynchronizeAsync(services, FolderMapping, cancellationToken);

        // Assert
        Assert.Equal(subjects.Count, result.Reconciliation.OwnMutationCompletedEmailCount);
        Assert.Equal(0, result.Reconciliation.RemotelyDeletedEmailCount);
        Assert.Empty(await mailbox.ReadAsync(FolderName, cancellationToken));

        // The mail the user asked to free space for is still theirs to read, which is the outcome that separates
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

    /// <summary>Every read the client surface publishes serves the retained copy and neither of the two the deployment tombstoned.</summary>
    /// <remarks>
    /// The retained message is the control each absence is read against: an answer that reported nothing at all would
    /// satisfy three of these assertions while proving that the reads had stopped working rather than that a tombstone
    /// takes a message out of them.
    /// </remarks>
    [Fact]
    public async Task ClientReads_AfterDeletesTheUserAuthored_ServeTheRetainedCopyAndNeitherTombstonedOne()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(ReadFolderName, cancellationToken);

        var run = Guid.NewGuid().ToString("N");
        var subjects = new Dictionary<AuthoredDeleteEmailDisposition, string>
        {
            [AuthoredDeleteEmailDisposition.RetainLocalCopy] = $"authored-delete-read-retained-{run}",
            [AuthoredDeleteEmailDisposition.RetainTombstone] = $"authored-delete-read-tombstoned-{run}",
            [AuthoredDeleteEmailDisposition.EraseLocalCopy] = $"authored-delete-read-erased-{run}",
        };

        foreach (var subject in subjects.Values)
        {
            await mailbox.AppendAsync(ReadFolderName, subject, ReadAuthor, [], cancellationToken);
        }

        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            RemotelyDeletedEmailDisposition.EraseLocalCopy);
        Assert.Equal(
            subjects.Count,
            (await SynchronizeAsync(services, ReadFolderMapping, cancellationToken)).StoredEmailCount);

        var stored = new Dictionary<AuthoredDeleteEmailDisposition, StoredEmailRow>();

        foreach (var (disposition, subject) in subjects)
        {
            stored[disposition] = await ReadStoredEmailAsync(services, subject, cancellationToken);

            var outcome = await DeleteAsync(
                services,
                ReadFolderMapping,
                stored[disposition],
                disposition,
                cancellationToken);
            Assert.Equal(MailboxMutationStatus.Performed, outcome.Status);
        }

        Assert.Equal(
            subjects.Count,
            (await SynchronizeAsync(services, ReadFolderMapping, cancellationToken))
                .Reconciliation
                .OwnMutationCompletedEmailCount);

        var retained = stored[AuthoredDeleteEmailDisposition.RetainLocalCopy];
        var tombstoned = stored[AuthoredDeleteEmailDisposition.RetainTombstone];
        var erased = stored[AuthoredDeleteEmailDisposition.EraseLocalCopy];

        // Act
        var timeline = await TimelineAsync(services, cancellationToken);
        var found = await SearchAsync(services, run, cancellationToken);
        var content = await ContentAsync(
            services,
            [retained.StoredEmailId, tombstoned.StoredEmailId, erased.StoredEmailId],
            cancellationToken);
        var retainedConversation = await ConversationAsync(services, retained, cancellationToken);
        var tombstonedConversation = await ConversationAsync(services, tombstoned, cancellationToken);
        var erasedConversation = await ConversationAsync(services, erased, cancellationToken);

        // Assert
        // The listing is narrowed to this test's own folder, so what it answers with is these three messages and the
        // one of them that survived is the whole of it.
        Assert.Equal(retained.StoredEmailId, Assert.Single(timeline));

        // The search is narrowed by the run this arrangement wrote into every one of the three subjects, so a term
        // that reached all three before the deletes reaches one after them.
        Assert.Equal(retained.StoredEmailId, Assert.Single(found));

        // The message read answers per identifier, which is what makes it the one read that reports the absence rather
        // than only omitting it: two of the three were named and neither came back with anything to read.
        StoredEmailId[] named = [retained.StoredEmailId, tombstoned.StoredEmailId, erased.StoredEmailId];
        StoredEmailId[] answered = [.. content.Emails.Select(outcome => outcome.StoredEmailId)];

        Assert.Equal(named, answered);
        Assert.NotNull(content.Emails[0].Content);
        Assert.Null(content.Emails[1].Content);
        Assert.Null(content.Emails[2].Content);

        // Each message is a conversation of one, so a conversation whose only message was tombstoned is answered the
        // way one this deployment never held is — which is what keeps a caller from reading which identifiers exist.
        Assert.NotNull(retainedConversation);
        Assert.Equal(retained.StoredEmailId, Assert.Single(retainedConversation.Messages).Email.StoredEmailId);
        Assert.Null(tombstonedConversation);
        Assert.Null(erasedConversation);
    }

    private static Task<MailboxSynchronizationResult> SynchronizeAsync(
        OrchestratedMailFathomServices services,
        MailFolderMapping folder,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxSynchronizer>().SynchronizeAsync(
                SyntheticMailAccount.Account,
                folder,
                token),
            cancellationToken);

    /// <summary>Deletes one stored email through the production performer, which writes the record the window reads.</summary>
    private static Task<MailboxMutationOutcome> DeleteAsync(
        OrchestratedMailFathomServices services,
        MailFolderMapping mapping,
        StoredEmailRow stored,
        AuthoredDeleteEmailDisposition localDisposition,
        CancellationToken cancellationToken)
    {
        var folder = MailFolderResolution.FirstBindingOf(mapping.Alias, mapping.RemotePath!.Value);
        var occurrence = EmailOccurrenceId.Create(
            SyntheticMailAccount.AccountId,
            folder.Id,
            stored.UidValidity,
            stored.Uid);
        var request = MailboxMutationRequest.Delete(
            stored.StoredEmailId, SyntheticMailAccount.User,
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
                    storedEmail.EmailThreadId,
                    storedEmail.RemoteExpungeObservedAt,
                    storedEmail.IsRetainedAfterAuthoredDelete))
                .SingleAsync(token),
            cancellationToken);

    /// <summary>Reads this test's own folder as the timeline serves it, under the grant every client read is published behind.</summary>
    private static async Task<IReadOnlyList<StoredEmailId>> TimelineAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var page = await services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailTimelineBrowser>().BrowsePageAsync(
                new BrowseTimelineRequest { Accounts = [ReadAccount], Folders = [ReadFolder] },
                token),
            [MailFathomPermission.MailRead],
            cancellationToken);

        return [.. page.Emails.Select(browsed => browsed.Email.StoredEmailId)];
    }

    /// <summary>Searches this test's own folder for the term its three subjects share.</summary>
    private static async Task<IReadOnlyList<StoredEmailId>> SearchAsync(
        OrchestratedMailFathomServices services,
        string queryText,
        CancellationToken cancellationToken)
    {
        var page = await services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailSearchBrowser>().SearchPageAsync(
                new BrowseSearchRequest { QueryText = queryText, Accounts = [ReadAccount], Folders = [ReadFolder] },
                token),
            [MailFathomPermission.MailRead],
            cancellationToken);

        return [.. page.Results.Select(result => result.Email.StoredEmailId)];
    }

    /// <summary>Reads the conversation one message was placed in, which is a conversation of one for each of the three.</summary>
    private static Task<BrowsedThread?> ConversationAsync(
        OrchestratedMailFathomServices services,
        StoredEmailRow stored,
        CancellationToken cancellationToken) => services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailThreadBrowser>().BrowsePageAsync(
                new BrowseThreadRequest { ThreadId = ConversationOf(stored) },
                token),
            [MailFathomPermission.MailRead],
            cancellationToken);

    /// <summary>Reads the named messages the way the client's reading pane does.</summary>
    private static Task<GetEmailContentResult> ContentAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken) => services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<EmailContentReader>().ReadContentAsync(
                GetEmailContentRequest.Create(storedEmailIds),
                token),
            [MailFathomPermission.MailRead],
            cancellationToken);

    /// <summary>Names the conversation a stored message was placed in, which storing assigns in the transaction that wrote the row.</summary>
    private static EmailThreadId ConversationOf(StoredEmailRow stored) => EmailThreadId.Create(
        stored.EmailThreadId ?? throw new InvalidOperationException(
            "The stored message was placed in no conversation, which storing never leaves behind."));

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
        Guid? EmailThreadId,
        DateTimeOffset? RemoteExpungeObservedAt,
        bool IsRetainedAfterAuthoredDelete);
}
