// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Resilience;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Mail.MailKit.Writes;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Resilience;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using MailKit.Net.Imap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>
/// Proves against a real IMAP server that MailFathom changes exactly what it was asked to change.
/// </summary>
/// <remarks>
/// <para>
/// No substituted protocol boundary can establish any of this. A unit test proves the adapter asked for
/// <c>UID EXPUNGE</c>; only a real server can show that the message beside it survived. The suite's own connections do
/// the seeding and the observing, so what a test reads back is the server's state rather than the adapter's belief
/// about it.
/// </para>
/// <para>
/// Three tests, because there are three claims real infrastructure is needed for: the native path, the fallback path,
/// and the scope of the expunge. Everything else about these mutations — which capability is required, what a refusal
/// reports, which flag is written — is a rule the unit suite already exercises against a substitute.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
[TestCaseOrderer(typeof(MailboxStateSequenceOrderer))]
public sealed class OrchestratedMailboxMutationTests(MailFathomOrchestrationFixture orchestration)
{
    private const string ArchiveFolderName = "MutationArchive";

    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create(OrchestratedMailbox.InboxPath, hierarchyDelimiter: '.'));

    /// <summary>The path the relocations below name, under the personal namespace GreenMail serves.</summary>
    private static readonly RemoteFolderPath ArchivePath =
        RemoteFolderPath.Create(ArchiveFolderName, hierarchyDelimiter: '.');

    /// <summary>
    /// The whole relocation, over the connection a deployment actually gets: the production registrations, the
    /// production pool, and a server that advertises <c>MOVE</c>. Filing a message must not mark it read, which is the
    /// half of the read-only guarantee that survives writing becoming possible.
    /// </summary>
    [Fact]
    [MailboxStateStep(1)]
    public async Task RelocateAsync_OnAServerAdvertisingMove_MovesTheEmailAndLeavesItUnread()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(ArchiveFolderName, cancellationToken);

        var subject = $"relocate-native-{Guid.NewGuid():N}";
        var occurrence = await DeliverAndLocateAsync(mailbox, subject, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var placement = await services.InScopeAsync(
            async (scope, token) =>
            {
                var account = SyntheticMailAccount.AccountId;
                await using var session = await scope.GetRequiredService<IMailboxWriteSessionFactory>()
                    .OpenForWritingAsync(
                        account,
                        Inbox,
                        scope.GetRequiredService<IMailTransportSecurityPolicyReader>().GetPolicy(account),
                        token);

                return await session.RelocateAsync(occurrence, ArchivePath, new InMemoryMailboxMutationJournal(), token);
            },
            cancellationToken);

        // Assert
        var inbox = await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken);
        var archived = await mailbox.ReadAsync(ArchiveFolderName, cancellationToken);
        var relocatedEmail = Assert.Single(archived, email => email.Subject == subject);

        Assert.DoesNotContain(inbox, email => email.Subject == subject);
        Assert.False(relocatedEmail.IsSeen);

        // GreenMail advertises UIDPLUS, so the server names the new occurrence rather than leaving it to be searched
        // for — and it names both halves of it. The UIDVALIDITY has to come out of that response: the destination
        // folder was resolved by path and never selected, so reading it there would report zero for a message that had
        // already moved.
        Assert.True(placement.IsReported);
        Assert.Equal(relocatedEmail.Uid.Value, placement.Uid?.Value);
        Assert.Equal(
            await mailbox.ReadUidValidityAsync(ArchiveFolderName, cancellationToken),
            placement.UidValidity);
    }

    /// <summary>
    /// The same relocation with <c>MOVE</c> hidden, which is the path every server without RFC 6851 takes. What it has
    /// to produce is a mailbox indistinguishable from the one above: the message in the destination folder, gone from
    /// the source, and still unread.
    /// </summary>
    [Fact]
    [MailboxStateStep(2)]
    public async Task RelocateAsync_OnAServerWithoutMove_ReachesTheSameStateThroughCopyFlagAndExpunge()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        var subject = $"relocate-fallback-{Guid.NewGuid():N}";
        var occurrence = await DeliverAndLocateAsync(mailbox, subject, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var placement = await services.InScopeAsync(
            (scope, token) => RelocateWithoutMoveExtensionAsync(
                scope,
                occurrence,
                new InMemoryMailboxMutationJournal(),
                token),
            cancellationToken);

        // Assert
        var inbox = await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken);
        var archived = await mailbox.ReadAsync(ArchiveFolderName, cancellationToken);
        var relocatedEmail = Assert.Single(archived, email => email.Subject == subject);

        Assert.DoesNotContain(inbox, email => email.Subject == subject);
        Assert.False(relocatedEmail.IsSeen);
        Assert.True(placement.IsReported);
        Assert.Equal(relocatedEmail.Uid.Value, placement.Uid?.Value);
        Assert.Equal(
            await mailbox.ReadUidValidityAsync(ArchiveFolderName, cancellationToken),
            placement.UidValidity);
    }

    /// <summary>
    /// The claim a bare <c>EXPUNGE</c> would break. Another client flags a neighbouring message <c>\Deleted</c> and
    /// leaves it in the folder; MailFathom then deletes a different message. The unscoped command would take both, so
    /// the neighbour still being there is what proves the expunge named a UID.
    /// </summary>
    [Fact]
    [MailboxStateStep(3)]
    public async Task DeleteAsync_WithANeighbourFlaggedDeletedBySomebodyElse_RemovesOnlyTheEmailItWasAsked()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        var neighbourSubject = $"expunge-neighbour-{Guid.NewGuid():N}";
        var targetSubject = $"expunge-target-{Guid.NewGuid():N}";

        var neighbour = await DeliverAndLocateAsync(mailbox, neighbourSubject, cancellationToken);
        var target = await DeliverAndLocateAsync(mailbox, targetSubject, cancellationToken);
        await mailbox.MarkDeletedAsync(OrchestratedMailbox.InboxPath, neighbour.Uid, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        await services.InScopeAsync(
            async (scope, token) =>
            {
                var account = SyntheticMailAccount.AccountId;
                await using var session = await scope.GetRequiredService<IMailboxWriteSessionFactory>()
                    .OpenForWritingAsync(
                        account,
                        Inbox,
                        scope.GetRequiredService<IMailTransportSecurityPolicyReader>().GetPolicy(account),
                        token);

                await session.DeleteAsync(target, new InMemoryMailboxMutationJournal(), token);

                return true;
            },
            cancellationToken);

        // Assert
        var inbox = await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken);

        Assert.DoesNotContain(inbox, email => email.Subject == targetSubject);
        Assert.Contains(inbox, email => email.Subject == neighbourSubject);
    }

    /// <summary>Builds the pool over a client that hides <c>MOVE</c>, and relocates one email through it.</summary>
    /// <remarks>
    /// The pool is constructed here rather than resolved, because the capability mask is the one thing this test has to
    /// control and the production registration hands the pool a plain <see cref="ImapClient" />. Everything else it is
    /// given comes out of the composed container, so what runs is the production connection, the production resilience
    /// pipelines, and the production session.
    /// </remarks>
    private static async Task<Domain.Mutations.RemoteEmailPlacement> RelocateWithoutMoveExtensionAsync(
        IServiceProvider scope,
        EmailOccurrenceId occurrence,
        InMemoryMailboxMutationJournal journal,
        CancellationToken cancellationToken)
    {
        var account = SyntheticMailAccount.AccountId;

        await using var pool = new MailboxWriteConnectionPool(
            () => CapabilityMaskedImapClient.HidingCapabilities(ImapCapabilities.Move),
            scope.GetRequiredService<IServiceScopeFactory>(),
            scope.GetRequiredService<OutboundOperationExecutor>(),
            scope.GetRequiredService<ITransientFailureClassifier>(),
            new MailboxWriteSessionOptions(),
            TimeProvider.System,
            NullLogger<MailboxWriteConnectionPool>.Instance);
        var telemetry = new MailboxMutationTelemetry(
            NullLogger<MailboxMutationTelemetry>.Instance,
            TimeProvider.System);

        var factory = new MailKitImapWriteSessionFactory(pool, telemetry);
        await using var session = await factory.OpenForWritingAsync(
            account,
            Inbox,
            scope.GetRequiredService<IMailTransportSecurityPolicyReader>().GetPolicy(account),
            cancellationToken);

        return await session.RelocateAsync(occurrence, ArchivePath, journal, cancellationToken);
    }

    /// <summary>Delivers one synthetic message and reads back the occurrence identity the server gave it.</summary>
    private static async Task<EmailOccurrenceId> DeliverAndLocateAsync(
        OrchestratedMailbox mailbox,
        string subject,
        CancellationToken cancellationToken)
    {
        await mailbox.DeliverAsync(subject, cancellationToken);

        var inbox = await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken);
        var delivered = Assert.Single(inbox, email => email.Subject == subject);

        return EmailOccurrenceId.Create(
            SyntheticMailAccount.AccountId,
            Inbox.Id,
            await mailbox.ReadUidValidityAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            delivered.Uid);
    }
}
