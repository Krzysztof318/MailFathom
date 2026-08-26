// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.AppHost;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Delivery;

/// <summary>Proves that a copy of a delivered message reaches the account's own folder once, and comes back as its own.</summary>
/// <remarks>
/// <para>
/// Three claims, and no substitute settles any of them. Whether an <c>APPEND</c> produces one message or two is the
/// server's answer rather than the adapter's; whether the copy coming back is recognized as this deployment's own runs
/// through a real synchronization over a real <c>APPENDUID</c>; and whether a rule pass then leaves it alone is a
/// partial index and a predicate PostgreSQL evaluates, over a column the same run wrote.
/// </para>
/// <para>
/// They are one test because they are one message going round one loop: the append, the discovery, and the queue are
/// consecutive states of the same copy, and a second test would have to reproduce the first one's mailbox to reach the
/// state it asserts on. What each state adds is asserted where it happens, so a failure names the step.
/// </para>
/// <para>
/// The folder holds the control the absence assertion needs. An ordinary message appended beside the copy travels the
/// same synchronization and the same queue read, so a queue that stopped reporting anything at all fails here rather
/// than passing as a suppression that works.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOutgoingMailFilingTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The one mailbox the orchestrated server has, which is both the sender and the addressee here.</summary>
    private const string Mailbox = OrchestrationContract.MailServerAccountEmailAddress;

    /// <summary>How much of the arrival queue one read takes, large enough that draining it costs a query or two.</summary>
    private const int ArrivalQueueBatchSize = 200;

    /// <summary>Bounds the paging loop. A queue that has not ended by then is a defect rather than a slow database.</summary>
    private const int MaximumArrivalQueueBatches = 200;

    private static readonly MailFolderMapping FiledCopyFolder = MailFolderMapping.ToRemotePath(
        MailFolderAlias.Create(SyntheticMailAccount.OutgoingCopyFolderAlias),
        RemoteFolderPath.Create(SyntheticMailAccount.OutgoingCopyFolderPath, hierarchyDelimiter: '.'));

    /// <summary>The whole loop a filed copy travels, from the append to the queue a rule pass reads.</summary>
    [Fact]
    public async Task RunAsync_ASendTheAccountFilesACopyOf_AppendsItOnceJoinsItAndLeavesItOutOfTheArrivalQueue()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        // Recreated rather than reused, so the copy this test counts is the only message the folder has ever held.
        await mailbox.RecreateFolderAsync(SyntheticMailAccount.OutgoingCopyFolderPath, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            filesSentCopies: true);

        // The binding a synchronization run over the sent folder would have recorded. Filing resolves its destination
        // by role and then reads that binding, and nothing has synchronized this folder yet, so without it the append
        // reports the destination as unavailable rather than reaching the server.
        await OrchestratedFolderBinding.CommitAsync(
            services,
            SyntheticMailAccount.OutgoingCopyFolderAlias,
            SyntheticMailAccount.OutgoingCopyFolderPath,
            cancellationToken);

        var subject = $"outgoing-filing-{Guid.NewGuid():N}";
        var arriving = $"outgoing-filing-arriving-{Guid.NewGuid():N}";
        var queued = await EnqueueAsync(services, subject, cancellationToken);

        // Act
        var report = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutboxPass>().RunAsync(
                SyntheticMailAccount.Account,
                token),
            cancellationToken);

        // The settlement asked for a second time, which is the call a restarted host would make. It is the one path
        // that could put a second copy of somebody's own message in their sent folder.
        var settledAgain = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<OutgoingMailFilingPass>().SettleFiledCopiesAsync(
                queued.Id,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(
            MailOutboxDeliveryOutcome.Sent,
            Assert.Single(report.Results, entry => entry.OutgoingEmailId == queued.Id).Outcome);
        Assert.Equal(
            OutgoingMailFilingOutcome.Filed,
            Assert.Single(report.FilingResults, entry => entry.OutgoingEmailId == queued.Id).Outcome);
        Assert.Equal(
            OutgoingMailFilingOutcome.AlreadyFiled,
            Assert.Single(settledAgain).Outcome);

        // The independent witness: one copy of the message in the folder, read over a connection nothing under test
        // owns, and read as the owner's own mail client would show it — sent mail is not unread mail.
        var filed = Assert.Single(
            await mailbox.ReadAsync(SyntheticMailAccount.OutgoingCopyFolderPath, cancellationToken),
            message => message.Subject == subject);
        Assert.True(filed.IsSeen);

        var record = await FindAsync(services, queued.Id, cancellationToken);
        var filing = record.FindFiling(OutgoingMailFiling.Sent);
        Assert.NotNull(filing);
        Assert.Equal(OutgoingMailFilingStage.Confirmed, filing.Stage);
        Assert.Equal(filed.Uid, filing.Placement.Uid);

        // A message nobody filed, in the same folder and through the same run, which is what makes the absence below an
        // observation rather than a queue that reports nothing.
        await mailbox.AppendAsync(SyntheticMailAccount.OutgoingCopyFolderPath, arriving, cancellationToken);

        var synchronization = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxSynchronizer>().SynchronizeAsync(
                SyntheticMailAccount.Account,
                FiledCopyFolder,
                token),
            cancellationToken);

        // At least the two this test wrote, rather than exactly them: the outbox pass is the account's rather than this
        // send's, so a message another class left queued is delivered by the same run and its copy is filed into this
        // folder beside them. What the folder holds in total is therefore the collection's rather than this test's, and
        // the two rows named below are what this run has to have stored.
        Assert.True(
            synchronization.StoredEmailCount >= 2,
            $"The run stored {synchronization.StoredEmailCount} emails, and the filed copy and the arriving message are two.");

        var stored = await ReadStoredAsync(services, cancellationToken);
        var storedCopy = Assert.Single(stored, email => email.Subject == subject);
        var storedArriving = Assert.Single(stored, email => email.Subject == arriving);
        Assert.Equal(queued.Id.Value, storedCopy.FiledFromOutgoingEmailId);
        Assert.Null(storedArriving.FiledFromOutgoingEmailId);

        // The filing is met: the copy the server named has been seen coming back, so nothing goes on looking for it.
        var observed = (await FindAsync(services, queued.Id, cancellationToken)).FindFiling(OutgoingMailFiling.Sent);
        Assert.NotNull(observed);
        Assert.NotNull(observed.ObservedAt);

        var awaitingEvaluation = await ReadArrivalQueueAsync(services, cancellationToken);
        Assert.DoesNotContain(StoredEmailId.Create(storedCopy.Id), awaitingEvaluation);
        Assert.Contains(StoredEmailId.Create(storedArriving.Id), awaitingEvaluation);
    }

    private static async Task<OutgoingEmailRecord> EnqueueAsync(
        OrchestratedMailFathomServices services,
        string subject,
        CancellationToken cancellationToken)
    {
        var opened = await services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>().EnqueueAsync(
                RequestFor(subject),
                MimeOf(subject),
                token),
            [MailFathomPermission.MailSend],
            cancellationToken);

        return opened.Record;
    }

    private static async Task<OutgoingEmailRecord> FindAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var record = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().FindAsync(outgoingEmailId, token),
            cancellationToken);

        Assert.NotNull(record);

        return record;
    }

    /// <summary>Reads what the folder's messages were stored as, including the join that says which of them is this system's own.</summary>
    private static Task<IReadOnlyList<StoredCopy>> ReadStoredAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) => (IReadOnlyList<StoredCopy>)await scope.GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(storedEmail => storedEmail.MailFolder.Alias == FiledCopyFolder.Alias.Value)
                .Select(storedEmail => new StoredCopy(
                    storedEmail.Id,
                    storedEmail.Subject,
                    storedEmail.FiledFromOutgoingEmailId))
                .ToArrayAsync(token),
            cancellationToken);

    /// <summary>Drains the queue a rule pass reads, which is shared with every other class in this collection.</summary>
    private static async Task<IReadOnlyList<StoredEmailId>> ReadArrivalQueueAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var queued = new List<StoredEmailId>();
        StoredEmailId? position = null;
        IReadOnlyList<StoredEmailAwaitingRuleEvaluation> batch;
        var batches = 0;

        do
        {
            batch = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IMailRuleEvaluationStore>()
                    .GetEmailsAwaitingFirstEvaluationAsync(
                        SyntheticMailAccount.Account,
                        position,
                        ArrivalQueueBatchSize,
                        token),
                cancellationToken);

            queued.AddRange(batch.Select(candidate => candidate.StoredEmailId));
            position = batch.Count == 0 ? position : batch[^1].StoredEmailId;
            batches++;
        }
        while (batch.Count > 0 && batches < MaximumArrivalQueueBatches);

        Assert.Empty(batch);

        return queued;
    }

    /// <summary>Addresses the send to the one mailbox the orchestrated server has, so a delivery is observable.</summary>
    private static OutgoingEmailRequest RequestFor(string invocationIdentity)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, Mailbox, out var recipient));

        return OutgoingEmailRequest.Create(
            SyntheticMailAccount.Account,
            OutgoingEmailRequester.Command(invocationIdentity),
            [OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To)]);
    }

    /// <summary>Builds a synthetic outgoing message whose subject is how a test recognizes its own copy.</summary>
    private static ReadOnlyMemory<byte> MimeOf(string subject) => Encoding.ASCII.GetBytes(
        $"Message-ID: <{subject}@mailfathom.test>\r\n"
        + $"From: {Mailbox}\r\n"
        + $"To: {Mailbox}\r\n"
        + $"Subject: {subject}\r\n"
        + "MIME-Version: 1.0\r\n"
        + "Content-Type: text/plain; charset=us-ascii\r\n\r\n"
        + "Synthetic body.\r\n")
        .AsMemory();

    /// <summary>One stored message as this test reads it back, projected rather than loaded.</summary>
    /// <param name="Id">The local identifier, which is what the arrival queue reports.</param>
    /// <param name="Subject">How the test recognizes which message it is.</param>
    /// <param name="FiledFromOutgoingEmailId">The send this message is a copy of, and <see langword="null" /> for arriving mail.</param>
    private sealed record StoredCopy(Guid Id, string? Subject, Guid? FiledFromOutgoingEmailId);
}
