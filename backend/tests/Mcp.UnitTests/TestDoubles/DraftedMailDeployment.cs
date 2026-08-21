// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Mcp.Tools.Drafts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Assembles the four draft tools over the real use cases, so a test drives a tool and reads what was written.</summary>
/// <remarks>
/// <para>
/// The tools call the use cases rather than substitutes for them, which is what makes a test about arguments a test
/// about the message they describe. What is substituted is everything below the application: the composition, because
/// MIME belongs to the MimeKit adapter's own suite, and the mail server, because a draft's copy belongs to the
/// integration suite.
/// </para>
/// <para>
/// No folder plays the drafts role here, so every draft is written and none is appended. That is the deployment a test
/// about a tool wants: what the tool answers is decided by the record, and whether a copy reached somebody's mailbox is
/// the filer's own suite.
/// </para>
/// </remarks>
internal sealed class DraftedMailDeployment
{
    /// <summary>The account every draft here belongs to, which is the one the catalog serves.</summary>
    internal const string ServedAccount = "work";

    /// <summary>The instant every record is stamped with, so a result's timestamp is a fact rather than a clock reading.</summary>
    internal static readonly DateTimeOffset Moment = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The message every composition here answers with, because no test about a tool reads MIME.</summary>
    private static readonly ReadOnlyMemory<byte> DraftedMime =
        "Message-ID: <one@example.test>\r\n\r\nHello."u8.ToArray().AsMemory();

    private readonly InMemoryMailDraftContentStore contents = new();

    /// <summary>The one clock every store and every use case here reads, so advancing it moves the whole deployment.</summary>
    private readonly FakeTimeProvider clock = new(Moment);

    /// <summary>Assembles the deployment for a caller granted exactly the permissions named.</summary>
    /// <param name="granted">What the entry that admitted the caller resolved to.</param>
    internal DraftedMailDeployment(params MailFathomPermission[] granted)
    {
        var authorization = AccessAuthorizations.ForCallerGranted(
            granted.Length == 0
                ? [MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend, MailFathomPermission.MailRead]
                : granted);

        var persistenceSessions = Substitute.For<IPersistenceSessionFactory>();
        persistenceSessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        var commitPolicy = new OptimisticConcurrencyRetryPolicy(
            persistenceSessions,
            new PersistenceConcurrencyOptions(),
            this.clock);

        var book = new MailDraftBook(
            this.Drafts,
            this.contents,
            commitPolicy,
            FilerReachingNoFolder(persistenceSessions, this.Drafts, this.contents, commitPolicy, this.clock),
            authorization,
            this.clock);

        var writing = new DraftedMailWriting(
            new AuthoredMailDrafting(
                new StubMailAccountCatalog(ServedAccount),
                new NamedRecipientResolver(Substitute.For<IContactDirectory>()),
                this.Composer,
                book,
                authorization),
            new AuthoredResponseDrafting(
                this.AuthoringOver(authorization),
                this.Composer,
                book,
                authorization));

        this.OutgoingEmails = new InMemoryOutgoingEmailStore(timeProvider: this.clock);
        this.SaveTool = new SaveDraftTool(writing);
        this.UpdateTool = new UpdateDraftTool(writing);
        this.DeleteTool = new DeleteDraftTool(book);
        this.SendTool = new SendDraftTool(new MailDraftPromotion(
            this.Drafts,
            this.contents,
            new MailOutbox(
                this.OutgoingEmails,
                Substitute.For<IEmailContentStore>(),
                commitPolicy,
                new MailOutboxSignal(capacity: 8),
                Substitute.For<IJobStore>(),
                Substitute.For<IOutboxOperationStore>(),
                authorization,
                OutgoingMailGovernors.Permitting(),
                this.clock),
            this.OutgoingEmails,
            commitPolicy,
            Bounds,
            AuthoredSendGovernors.Permitting(authorization),
            authorization));
    }

    /// <summary>Gets what has been written down about every draft, which is what a tool's answer is read against.</summary>
    internal InMemoryMailDraftStore Drafts { get; } = new();

    /// <summary>Gets the composition every draft passes through, so a test can read the message the arguments became.</summary>
    internal IAuthoredEmailComposer Composer { get; } = ComposingAuthoredEmails.ThatComposesDrafts(DraftedMime);

    /// <summary>Gets the summaries an answered email is read from, which hold nothing until a test says otherwise.</summary>
    internal IStoredEmailSummaryReader Summaries { get; } = Substitute.For<IStoredEmailSummaryReader>();

    /// <summary>Gets the store the promotion writes a send into and reads one back from.</summary>
    internal InMemoryOutgoingEmailStore OutgoingEmails { get; }

    /// <summary>Gets the tool that writes a draft.</summary>
    internal SaveDraftTool SaveTool { get; }

    /// <summary>Gets the tool that replaces one.</summary>
    internal UpdateDraftTool UpdateTool { get; }

    /// <summary>Gets the tool that gives one up.</summary>
    internal DeleteDraftTool DeleteTool { get; }

    /// <summary>Gets the tool that sends one.</summary>
    internal SendDraftTool SendTool { get; }

    /// <summary>Gets the message the composition was last handed, which is what the caller's arguments became.</summary>
    internal AuthoredEmail ComposedMessage =>
        (AuthoredEmail)this.Composer.ReceivedCalls().Last().GetArguments()[1]!;

    /// <summary>What this deployment sends, which is generous enough that no test meets it by accident.</summary>
    private static OutgoingEmailBounds Bounds => new()
    {
        MaxRecipientCount = 8,
        MaxBodyCharacters = 4096,
        MaxAttachmentCount = 3,
        MaxAttachmentBytes = 4096,
        MaxMessageBytes = 65536,
    };

    /// <summary>Builds a filer for an account that maps no folder to the drafts role, so nothing is ever appended.</summary>
    private static MailDraftFiler FilerReachingNoFolder(
        IPersistenceSessionFactory persistenceSessions,
        IMailDraftStore drafts,
        IEmailContentStore contents,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider clock)
    {
        var transportSecurityPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
        var folderResolutions = Substitute.For<IMailFolderResolutionStore>();
        var writeSessions = Substitute.For<IMailboxWriteSessionFactory>();

        var destinations = new MailboxDestinationResolver(
            StubMailFolderMappings.Nothing.Resolver,
            folderResolutions,
            new MailFolderResolver(
                Substitute.For<IRemoteFolderCatalog>(),
                Substitute.For<IRemoteFolderCreator>(),
                folderResolutions,
                Substitute.For<IMailFolderMappingChangeAuditor>(),
                persistenceSessions,
                clock),
            transportSecurityPolicies);

        return new MailDraftFiler(
            new MailboxCopyAppender(writeSessions, destinations, contents, transportSecurityPolicies, clock),
            writeSessions,
            destinations,
            drafts,
            transportSecurityPolicies,
            commitPolicy,
            clock);
    }

    /// <summary>Builds the authoring an answer is derived through, over a mailbox holding whatever a test put in it.</summary>
    private StoredEmailResponseAuthoring AuthoringOver(AccessAuthorization authorization)
    {
        var catalog = new StubMailAccountCatalog(ServedAccount);

        return new StoredEmailResponseAuthoring(
            this.Summaries,
            Substitute.For<IEmailContentStore>(),
            Substitute.For<IEmailContentRenderer>(),
            Substitute.For<IEmailAttachmentContentReader>(),
            Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Nothing,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            Substitute.For<IOutgoingSenderIdentityReader>(),
            new NamedRecipientResolver(Substitute.For<IContactDirectory>()),
            Bounds,
            authorization);
    }
}
