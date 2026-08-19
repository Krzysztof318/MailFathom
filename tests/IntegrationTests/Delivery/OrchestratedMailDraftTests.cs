// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.AppHost;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Delivery;

/// <summary>Proves that editing a draft leaves one message in the folder, that sending it leaves none, and that the owner's own draft survives both.</summary>
/// <remarks>
/// <para>
/// Three claims, and no substitute settles any of them. Whether replacing a stored message leaves one draft or two is
/// the server's answer to an <c>APPEND</c> followed by a <c>UID EXPUNGE</c> rather than the adapter's; whether the copy
/// a promotion gives up is the one that append reported runs through a real <c>APPENDUID</c>; and whether a message
/// nobody here wrote is still in the folder afterwards is a read over a connection nothing under test owns.
/// </para>
/// <para>
/// They are one test because they are one draft going round one loop: the append, the replacement, the promotion, and
/// the delivery are consecutive states of the same message, and a second test would have to reproduce the first one's
/// folder to reach the state it asserts on. What each state adds is asserted where it happens, so a failure names the
/// step.
/// </para>
/// <para>
/// The folder holds the control every absence assertion needs. A draft appended beside MailFathom's own — same folder,
/// same <c>\Draft</c> flag, reachable by the same commands — is what makes "one draft left" an observation rather than
/// a removal that emptied the folder.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailDraftTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The one mailbox the orchestrated server has, which is both the sender and the addressee here.</summary>
    private const string Mailbox = OrchestrationContract.MailServerAccountEmailAddress;

    /// <summary>The domain every identity this test mints belongs to, which is reserved and reaches nothing.</summary>
    private const string MessageIdDomain = "mailfathom.test";

    /// <summary>The whole loop a draft travels, from the first append to the folder a delivered send leaves behind.</summary>
    [Fact]
    public async Task SaveAsync_ADraftEditedAndThenPromoted_LeavesOneDraftThenNoneAndSparesTheOwnersOwn()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        // Recreated rather than reused, so the messages this test counts are the only ones the folders have ever held.
        await mailbox.RecreateFolderAsync(SyntheticMailAccount.DraftCopyFolderPath, cancellationToken);
        await mailbox.RecreateFolderAsync(SyntheticMailAccount.OutgoingCopyFolderPath, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            filesSentCopies: true,
            keepsDrafts: true);

        var written = $"draft-{Guid.NewGuid():N}";
        var revised = $"draft-revised-{Guid.NewGuid():N}";
        var foreign = $"draft-foreign-{Guid.NewGuid():N}";

        // The draft MailFathom did not write, appended before anything else reaches the folder so that every command
        // the run afterwards issues has had the chance to take it.
        await mailbox.AppendDraftAsync(SyntheticMailAccount.DraftCopyFolderPath, foreign, cancellationToken);
        var owners = Assert.Single(await ReadDraftsAsync(mailbox, cancellationToken), Named(foreign));

        // Act
        var draft = await SaveAsync(services, written, revises: null, cancellationToken);

        // Assert
        Assert.Equal(MailDraftStage.Filed, draft.Stage);

        var appended = Assert.Single(await ReadDraftsAsync(mailbox, cancellationToken), Named(written));
        Assert.True(appended.IsDraft);
        Assert.Equal(appended.Uid, draft.CurrentCopy?.Placement.Uid);

        // The edit: one append and one removal, and the folder is what says whether both of them happened.
        var edited = await SaveAsync(services, revised, draft.Id, cancellationToken);

        Assert.Equal(MailDraftStage.Filed, edited.Stage);
        Assert.Equal(2, edited.Revision);
        Assert.Null(edited.Divergence);

        var afterEdit = await ReadDraftsAsync(mailbox, cancellationToken);
        Assert.True(Assert.Single(afterEdit, Named(revised)).IsDraft);
        Assert.DoesNotContain(afterEdit, Named(written));
        Assert.Contains(afterEdit, Named(foreign));

        // The promotion, which writes an ordinary outgoing record and leaves the draft where it is until it is sent.
        var promoted = await PromoteAsync(services, edited.Id, cancellationToken);

        Assert.Equal(promoted.Id, (await FindAsync(services, edited.Id, cancellationToken))?.PromotedTo);
        Assert.Contains(await ReadDraftsAsync(mailbox, cancellationToken), Named(revised));

        var report = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutboxPass>().RunAsync(
                SyntheticMailAccount.AccountId,
                token),
            cancellationToken);

        Assert.Equal(
            MailOutboxDeliveryOutcome.Sent,
            Assert.Single(report.Results, entry => entry.OutgoingEmailId == promoted.Id).Outcome);
        Assert.Equal(
            MailDraftFilingOutcome.Discarded,
            Assert.Single(report.DraftResults, entry => entry.DraftId == edited.Id).Outcome);
        Assert.Equal(
            OutgoingMailFilingOutcome.Filed,
            Assert.Single(report.FilingResults, entry => entry.OutgoingEmailId == promoted.Id).Outcome);

        // The independent witness on both folders: the drafts folder holds the owner's own message and nothing else,
        // still under the UID it was appended with, and what was drafted is in the sent folder exactly once.
        var remaining = Assert.Single(await ReadDraftsAsync(mailbox, cancellationToken));
        Assert.Equal(foreign, remaining.Subject);
        Assert.Equal(owners.Uid, remaining.Uid);
        Assert.True(remaining.IsDraft);

        Assert.Single(
            await mailbox.ReadAsync(SyntheticMailAccount.OutgoingCopyFolderPath, cancellationToken),
            Named(revised));

        // The record goes with the copy: a draft that has been sent is no longer a draft this deployment holds.
        Assert.Null(await FindAsync(services, edited.Id, cancellationToken));
    }

    /// <summary>Recognizes one of this test's own messages among whatever the folder holds.</summary>
    private static Predicate<ObservedEmail> Named(string subject) => message => message.Subject == subject;

    private static Task<IReadOnlyList<ObservedEmail>> ReadDraftsAsync(
        OrchestratedMailbox mailbox,
        CancellationToken cancellationToken) =>
        mailbox.ReadAsync(SyntheticMailAccount.DraftCopyFolderPath, cancellationToken);

    /// <summary>Writes a draft down as a caller holding the grant a send is admitted under, which is what a command is.</summary>
    private static Task<MailDraftRecord> SaveAsync(
        OrchestratedMailFathomServices services,
        string subject,
        MailDraftId? revises,
        CancellationToken cancellationToken)
    {
        var messageId = InternetMessageId.Mint(MessageIdDomain);

        return services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailDraftBook>().SaveAsync(
                SyntheticMailAccount.AccountId,
                OutgoingEmailRequester.Command(subject),
                new ComposedMailDraft([RecipientAtTheMailbox()], messageId, MimeOf(subject, messageId)),
                revises,
                token),
            [MailFathomPermission.MailSend],
            cancellationToken);
    }

    private static Task<OutgoingEmailRecord> PromoteAsync(
        OrchestratedMailFathomServices services,
        MailDraftId draftId,
        CancellationToken cancellationToken) => services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailDraftPromotion>().PromoteAsync(
                draftId,
                OutgoingEmailRequester.Command($"promote-{draftId.Value:N}"),
                token),
            [MailFathomPermission.MailSend],
            cancellationToken);

    private static Task<MailDraftRecord?> FindAsync(
        OrchestratedMailFathomServices services,
        MailDraftId draftId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailDraftStore>().FindAsync(draftId, token),
            cancellationToken);

    /// <summary>Addresses the draft to the one mailbox the orchestrated server has, so a promotion is deliverable.</summary>
    private static OutgoingRecipient RecipientAtTheMailbox()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, Mailbox, out var recipient));

        return OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To);
    }

    /// <summary>Builds a synthetic message whose subject is how a test recognizes its own draft.</summary>
    private static ReadOnlyMemory<byte> MimeOf(string subject, InternetMessageId messageId) => Encoding.ASCII.GetBytes(
        $"Message-ID: <{messageId.Value}>\r\n"
        + $"From: {Mailbox}\r\n"
        + $"To: {Mailbox}\r\n"
        + $"Subject: {subject}\r\n"
        + "MIME-Version: 1.0\r\n"
        + "Content-Type: text/plain; charset=us-ascii\r\n\r\n"
        + "Synthetic body.\r\n")
        .AsMemory();
}
