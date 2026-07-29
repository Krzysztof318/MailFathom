// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Mail;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.IntegrationTests.Mailbox;
using MailMcp.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailMcp.IntegrationTests.Synchronization;

/// <summary>Proves against a real IMAP server that reading mail never marks it read.</summary>
/// <remarks>
/// This is the claim the repository makes constantly and that no unit test can reach: every one of those substitutes a
/// port, so it proves the application asked for the seen-preserving operation and can say nothing about what the
/// resulting IMAP commands did to the server. Here the flag is read back from the server itself, over a connection the
/// adapter under test never touches.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
[TestCaseOrderer(typeof(MailboxStateSequenceOrderer))]
public sealed class RemoteSeenFlagPreservationTests(MailMcpOrchestrationFixture orchestration)
{
    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create(OrchestratedMailbox.InboxPath, hierarchyDelimiter: '.'));

    /// <summary>Establishes that the suite's own observation can see a flag the server holds.</summary>
    /// <remarks>
    /// Runs first because every later assertion in this class is that a flag is <em>not</em> set, and a server that
    /// never recorded the flag — or an observation that never read it — would satisfy all of them. This is the test
    /// that fails in that case, and it is deliberately the cheapest one to read when the suite starts reporting that
    /// everything passes.
    /// </remarks>
    [Fact]
    [MailboxStateStep(1)]
    public async Task MarkSeenAsync_OnADeliveredMessage_IsObservedByTheSuitesOwnConnection()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        var subject = $"seen-flag-control-{Guid.NewGuid():N}";
        await mailbox.DeliverAsync(subject, cancellationToken);

        var delivered = RemoteSeenFlagAssertion.SeededBy(
            await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            [subject]);
        Assert.False(delivered[0].IsSeen);

        // Act
        await mailbox.MarkSeenAsync(OrchestratedMailbox.InboxPath, delivered[0].Uid, cancellationToken);

        // Assert
        var marked = RemoteSeenFlagAssertion.SeededBy(
            await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            [subject]);
        Assert.True(marked[0].IsSeen);
    }

    /// <summary>Reads two unread messages whole through the adapter and requires the server to still call them unread.</summary>
    /// <remarks>
    /// One session covers both operations the adapter performs against a folder — the metadata batch and the content
    /// fetch — because both run over the same selection and either one could set the flag. Splitting them would double
    /// the cost of the suite to separate two answers that arrive together anyway.
    /// </remarks>
    [Fact]
    [MailboxStateStep(2)]
    public async Task FetchEmailContentWithoutSettingSeenAsync_AgainstARealServer_LeavesEveryRemoteSeenFlagUnset()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        var subjects = Enumerable.Range(1, 2)
            .Select(index => $"seen-flag-preservation-{index}-{Guid.NewGuid():N}")
            .ToArray();

        // The checkpoint the session reads past, captured before the delivery, so this test reads its own messages and
        // not whatever an earlier test left in a mailbox that outlives it.
        var priorEmails = await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken);
        var checkpointUid = priorEmails.Count == 0 ? null : (ImapUid?)priorEmails[^1].Uid;

        foreach (var subject in subjects)
        {
            await mailbox.DeliverAsync(subject, cancellationToken);
        }

        await using var services = await OrchestratedMailMcpServices.StartAsync(orchestration, cancellationToken);

        // Act
        var fetchedContentLengths = await services.InScopeAsync(
            async (scope, token) =>
            {
                var account = SyntheticMailAccount.AccountId;
                var sessionFactory = scope.GetRequiredService<IMailboxSessionFactory>();
                await using var session = await sessionFactory.OpenReadOnlyAsync(
                    account,
                    Inbox,
                    scope.GetRequiredService<IMailTransportSecurityPolicyReader>().GetPolicy(account),
                    token);

                var batch = await session.GetEmailBatchAfterAsync(checkpointUid, maxEmailCount: 50, token);

                var contentLengths = new List<int>();
                foreach (var email in batch.Emails)
                {
                    var fetch = await session.FetchEmailContentWithoutSettingSeenAsync(
                        email.OccurrenceId,
                        maxRawMimeBytes: 1024L * 1024L,
                        token);

                    contentLengths.Add(fetch.Content?.RawMime.Length ?? 0);
                }

                return contentLengths;
            },
            cancellationToken);

        // Assert
        var observedEmails = RemoteSeenFlagAssertion.SeededBy(
            await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            subjects);

        Assert.Equal(subjects.Length, fetchedContentLengths.Count);
        Assert.All(fetchedContentLengths, contentLength => Assert.True(contentLength > 0));
        RemoteSeenFlagAssertion.AssertNoneIsSeen(
            observedEmails,
            "Reading a metadata batch and fetching every message's content through IMailboxSession");
    }
}
