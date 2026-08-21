// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Composes the use case the reply and forward tools call, over stores that hold everything in memory.</summary>
/// <remarks>
/// <para>
/// Both tools call the real <see cref="AuthoredResponseSubmission" /> rather than a substitute for it, so what a tool
/// test proves is that the arguments a caller sends reach the use case as the answer they describe. Composing it takes
/// the whole authoring graph, which is why the arrangement lives here rather than being written twice: two tools
/// arranging the same graph apart would let one of them prove its mapping against a deployment the other does not have.
/// </para>
/// <para>
/// Only the parts a tool test says nothing about are substituted — MIME belongs to the MimeKit adapter's own suite and
/// a durable row to the database's — so the composer here records what it was handed and answers with a message built
/// from it.
/// </para>
/// </remarks>
internal static class AnsweredMailSubmissions
{
    /// <summary>The account the answered email is stored from, which is the account an answer is sent as.</summary>
    public const string ServedAccountId = "primary";

    /// <summary>The folder the answered email is stored in, which this deployment maps for tools.</summary>
    public const string ReadableFolderAlias = "INBOX";

    /// <summary>The address the answering account sends from, which a reply to all leaves out.</summary>
    public const string SendingAddress = "mailfathom@example.test";

    /// <summary>The address the answered message was written from, which every reply is addressed to.</summary>
    public const string AnsweredAuthorAddress = "author@example.test";

    /// <summary>The address the answered message copied, which only a reply to all keeps.</summary>
    public const string AnsweredCopiedAddress = "colleague@example.test";

    /// <summary>When the record every answer is queued as was written down.</summary>
    public static DateTimeOffset RecordedAt { get; } = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] StoredRawMime =
        Encoding.UTF8.GetBytes("From: author@example.test\r\n\r\nThe report is attached.");

    private static readonly ReadOnlyMemory<byte> ComposedMime =
        Encoding.ASCII.GetBytes("Message-ID: <one@example.test>\r\n\r\nThank you.").AsMemory();

    private static readonly ReadOnlyMemory<byte> CarriedOctets = Encoding.ASCII.GetBytes("carried!").AsMemory();

    /// <summary>Composes the use case both response tools are built over.</summary>
    /// <param name="composer">Receives the composer the answer is composed through, which is what a mapping is read from.</param>
    /// <param name="summary">The answered email, or <see langword="null" /> for the ordinary one.</param>
    /// <param name="rendering">What the stored copy renders as, or <see langword="null" /> for an ordinary message.</param>
    /// <param name="participationReader">What this deployment maps for tools, or <see langword="null" /> to map the answered folder.</param>
    /// <param name="authorization">What the caller was granted, or <see langword="null" /> for both grants an answer needs.</param>
    /// <returns>The use case.</returns>
    public static AuthoredResponseSubmission Over(
        out IAuthoredEmailComposer composer,
        EmailSummary? summary = null,
        EmailContentRendering? rendering = null,
        IMailFolderParticipationReader? participationReader = null,
        AccessAuthorization? authorization = null)
    {
        var answered = summary ?? AnsweredEmail();
        composer = ComposingAuthoredEmails.ThatComposes(ComposedMime);
        var granted = authorization
            ?? AccessAuthorizations.ForCallerGranted(
                MailFathomPermission.MailSend,
                MailFathomPermission.MailRead);

        var authoring = new StoredEmailResponseAuthoring(
            new StubStoredEmailSummaryReader(answered),
            new StubEmailContentStore(IntactContent()),
            new StubEmailContentRenderer(
                EmailContentRenderingResult.Rendered(rendering ?? AnsweredRendering())),
            AttachmentReaderOpening(),
            Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                new StubMailAccountCatalog(ServedAccountId),
                participationReader ?? MappedInbox,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            SenderIdentities(),
            new NamedRecipientResolver(Substitute.For<IContactDirectory>()),
            Bounds(),
            granted);

        return new AuthoredResponseSubmission(
            authoring,
            composer,
            Outbox(granted),
            AuthoredSendGovernors.Permitting(granted),
            granted);
    }

    /// <summary>Reads the message an answer was composed from, which is where a tool's mapping becomes observable.</summary>
    /// <param name="composer">The composer the answer was composed through.</param>
    /// <returns>The authored message.</returns>
    public static AuthoredEmail ComposedAnswer(IAuthoredEmailComposer composer)
    {
        ArgumentNullException.ThrowIfNull(composer);

        return (AuthoredEmail)composer
            .ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IAuthoredEmailComposer.Compose))
            .GetArguments()[2]!;
    }

    /// <summary>Builds the summary of the email an answer is anchored to.</summary>
    /// <param name="contentAvailability">What this deployment holds of its content.</param>
    /// <returns>The summary.</returns>
    public static EmailSummary AnsweredEmail(
        StoredEmailContentAvailability contentAvailability = StoredEmailContentAvailability.Available) => new()
        {
            StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
            AccountId = MailAccountId.Create(ServedAccountId),
            FolderAlias = MailFolderAlias.Create(ReadableFolderAlias),
            InternetMessageId = "<parent@example.test>",
            Subject = "Quarterly report",
            SenderAddress = AnsweredAuthorAddress,
            SenderDisplayName = null,
            ToAddresses = [SendingAddress],
            SentAt = null,
            ReceivedAt = null,
            SizeOctets = 4096,
            Attachments = StoredEmailAttachmentSummary.None,
            ContentAvailability = contentAvailability,
            RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
            SenderVerification = SenderVerification.NotEstablished,
            MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
            SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
        };

    /// <summary>Builds what the answered email's stored copy renders as.</summary>
    /// <param name="attachments">The files it carries, or <see langword="null" /> for a message carrying none.</param>
    /// <returns>The rendering.</returns>
    public static EmailContentRendering AnsweredRendering(
        IReadOnlyList<ExtractedEmailAttachment>? attachments = null)
    {
        const string PlainText = "The report is attached.";
        var carried = attachments ?? [];

        return new EmailContentRendering(
            new EmailContentHeaders(
                "Quarterly report",
                null,
                null,
                [
                    Participant(EmailAddressRole.From, AnsweredAuthorAddress),
                    Participant(EmailAddressRole.To, SendingAddress),
                    Participant(EmailAddressRole.Cc, AnsweredCopiedAddress),
                ],
                EmailThreadReferences.Create("parent@example.test", inReplyTo: null, references: null)),
            new EmailBodyRepresentation(PlainText, PlainText.Length, EmailBodyTruncation.None),
            null,
            false,
            EmailAttachmentSummary.Create(
                carried,
                inlineResourceCount: 0,
                false,
                carriesUnverifiedSignature: false,
                containsUnexpandedTnefPart: false),
            carried);
    }

    /// <summary>Describes one file the answered message carries, as a forward reads it before carrying it.</summary>
    /// <param name="fileName">The name the sender gave it.</param>
    /// <param name="sizeOctets">How large it decodes to.</param>
    /// <returns>The description.</returns>
    public static ExtractedEmailAttachment CarriedFile(string fileName, long sizeOctets) => new(
        AttachmentFileName.TryNormalize(fileName, out var normalized) ? normalized : null,
        "application/pdf",
        sizeOctets);

    /// <summary>What this deployment is willing to compose, which is what refuses a forward carrying too much.</summary>
    /// <returns>The bounds.</returns>
    public static OutgoingEmailBounds Bounds() => new()
    {
        MaxRecipientCount = 8,
        MaxBodyCharacters = 4096,
        MaxAttachmentCount = 3,
        MaxAttachmentBytes = 128,
        MaxMessageBytes = 300,
    };

    /// <summary>The one folder this deployment maps, which is what makes the answered email readable at all.</summary>
    private static StubMailFolderParticipation MappedInbox => StubMailFolderParticipation.Mapping(
        new MailFolderIdentity(MailAccountId.Create(ServedAccountId), MailFolderAlias.Create(ReadableFolderAlias)));

    private static EmailParticipant Participant(EmailAddressRole role, string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress)
            ? new EmailParticipant(role, emailAddress)
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");

    private static IOutgoingSenderIdentityReader SenderIdentities()
    {
        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();

        if (!EmailAddress.TryCreate("MailFathom", SendingAddress, out var address))
        {
            throw new InvalidOperationException("The test sending address names no mailbox.");
        }

        senderIdentities
            .FindSenderIdentity(Arg.Any<MailAccountId>())
            .Returns(OutgoingSenderIdentity.Create(MailAccountId.Create(ServedAccountId), address));

        return senderIdentities;
    }

    private static IEmailAttachmentContentReader AttachmentReaderOpening()
    {
        var attachmentReader = Substitute.For<IEmailAttachmentContentReader>();
        attachmentReader
            .OpenAsync(Arg.Any<StoredEmailContent>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(
                OpenedEmailAttachmentResult.Opened(new StubOpenedEmailAttachment())));

        return attachmentReader;
    }

    private static MailOutbox Outbox(AccessAuthorization granted)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new MailOutbox(
            new InMemoryOutgoingEmailStore(timeProvider: new FakeTimeProvider(RecordedAt)),
            Substitute.For<IEmailContentStore>(),
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider()),
            new MailOutboxSignal(capacity: 8),
            Substitute.For<IJobStore>(),
            Substitute.For<IOutboxOperationStore>(),
            granted,
            OutgoingMailGovernors.Permitting(),
            new FakeTimeProvider(RecordedAt));
    }

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress)
            ? emailAddress
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");

    private static StoredEmailContent IntactContent() =>
        new(StoredRawMime, StoredRawMime.Length, SHA256.HashData(StoredRawMime));

    /// <summary>An opened attachment carrying a few octets, which is all a forward needs to be composed from here.</summary>
    private sealed class StubOpenedEmailAttachment : IOpenedEmailAttachment
    {
        public ExtractedEmailAttachment Description { get; } = CarriedFile("carried.pdf", sizeOctets: 8);

        public Task WriteContentToAsync(Stream destination, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destination);

            return destination.WriteAsync(CarriedOctets, cancellationToken).AsTask();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
