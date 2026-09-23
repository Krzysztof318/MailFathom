// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.AI.AgentConversations;
using MailFathom.Application.Access;
using MailFathom.Application.Calendar;
using MailFathom.Application.Contacts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Observability;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Evaluations.Answering;
using MailFathom.Evaluations.Corpus;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.TestSupport;

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>The use cases a deployment gives the Agent's tools, composed over the whole synthetic corpus and the person's agenda instead of over a database.</summary>
/// <remarks>
/// <para>
/// Every reader is the deployment's own class — the content reader, the state browser, the calendar, the task list, and the
/// authoring of an answer — so what a tool hands the model is what a deployment's would: a message rendered from its MIME
/// by the deployment's renderer, a conversation assembled and ordered by the deployment's reader, a state led by its own
/// rule. What stands in for the database is only the port beneath each of them, holding every corpus message in one inbox
/// of one account, each conversation as one thread, and the agenda <see cref="PersonalAgenda" /> writes.
/// </para>
/// <para>
/// Nothing is ever written here: every port refuses a write, because the Agent proposes and a proposal is the conversation's
/// to hold rather than the mailbox's.
/// </para>
/// </remarks>
internal static class CorpusReaders
{
    private static readonly OutgoingEmailBounds DeploymentBounds = new MailDeliveryOptions().ToOutgoingEmailBounds();

    private static readonly Lazy<IReadOnlyList<IReadOnlyList<CorpusMessage>>> Exchanges = new(static () =>
        [.. CorpusMessage.Exchanges, .. HostileMail.Exchanges, .. PolishCorpus.Exchanges]);

    private static readonly Lazy<IReadOnlyDictionary<StoredEmailId, EmailSummary>> Summaries = new(static () =>
        Exchanges.Value
            .SelectMany(static exchange => exchange.Select(message => SummaryOf(message, ThreadOf(exchange))))
            .ToDictionary(static summary => summary.StoredEmailId));

    /// <summary>Names the thread a conversation is stored as, distinct from every message's identifier and the same on every run.</summary>
    /// <param name="exchange">The conversation.</param>
    /// <returns>The thread's identifier.</returns>
    public static EmailThreadId ThreadOf(IReadOnlyList<CorpusMessage> exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);

        var first = exchange[0].Id.Value.ToByteArray();

        return EmailThreadId.Create(new Guid(BitConverter.ToInt32(first, 0), 1, 0, new byte[8]));
    }

    /// <summary>Composes the readers a deployment would hand one person's run, under the grant they hold.</summary>
    /// <param name="authorization">The grant the run acts under.</param>
    /// <param name="search">The search the run's lookups reach.</param>
    /// <returns>The readers.</returns>
    public static AgentConversationReaders For(AccessAuthorization authorization, IEmailKnowledgeSearch search)
    {
        var scope = ScopeFor(authorization);
        var summaries = new CorpusSummaries();
        var content = new CorpusContent();
        var renderer = new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions());
        var repairs = new NoRepairs();

        return new AgentConversationReaders(
            scope,
            search,
            ContentReaderOver(scope, authorization),
            new MailThreadStateBrowser(PersonalAgenda.ThreadStateReader(), scope, SensitiveContentEgressGuards.Inactive(), authorization),
            new OwnCalendar(
                authorization,
                PersonalAgenda.CalendarStore(),
                new OptimisticConcurrencyRetryPolicy(new NoSessions(), new PersistenceConcurrencyOptions(), TimeProvider.System),
                TimeProvider.System),
            new OwnTasks(authorization, PersonalAgenda.TaskStore(), TimeProvider.System),
            new StoredEmailResponseAuthoring(
                summaries,
                content,
                renderer,
                new NoAttachmentContent(),
                repairs,
                scope,
                new OwnAddress(),
                new NamedRecipientResolver(new NoContacts(), ContactBookOwnerships.For(authorization, CorpusKnowledgeSearch.Account)),
                DeploymentBounds,
                authorization),
            authorization);
    }

    /// <summary>Composes the content reader a deployment would hand one person, under the grant they hold.</summary>
    /// <param name="authorization">The grant the read acts under.</param>
    /// <returns>The reader.</returns>
    public static EmailContentReader ContentReaderFor(AccessAuthorization authorization) =>
        ContentReaderOver(ScopeFor(authorization), authorization);

    /// <summary>Reads one message as a listing shows it, from the one inbox every corpus message is delivered to.</summary>
    /// <param name="message">The message.</param>
    /// <param name="thread">The thread it is stored in, or <see langword="null" /> where the reader needs none.</param>
    /// <returns>The summary.</returns>
    internal static EmailSummary SummaryOf(CorpusMessage message, EmailThreadId? thread) =>
        new()
        {
            StoredEmailId = message.Id,
            Account = CorpusKnowledgeSearch.Account,
            FolderAlias = CorpusKnowledgeSearch.Inbox,
            ThreadId = thread,
            Subject = message.Subject,
            SentAt = message.ReceivedAt,
            ReceivedAt = message.ReceivedAt,
            SizeOctets = message.RawMime.Length,
            SenderDisplayName = message.SenderName,
            SenderAddress = message.Sender,
            ToAddresses = message.Recipients,
            SenderVerification = SenderVerification.NotEstablished,
            SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
            MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
            Attachments = StoredEmailAttachmentSummary.None,
            ContentAvailability = StoredEmailContentAvailability.Available,
            RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
        };

    private static MailboxScopeResolver ScopeFor(AccessAuthorization authorization) =>
        new(
            AssignedMailAccountCatalogs.For(authorization, SyntheticServedAccount.Of(CorpusKnowledgeSearch.Account)),
            StubMailFolderParticipation.Mapping(new MailFolderIdentity(CorpusKnowledgeSearch.Account, CorpusKnowledgeSearch.Inbox)),
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing);

    private static EmailContentReader ContentReaderOver(MailboxScopeResolver scope, AccessAuthorization authorization) =>
        new(
            new CorpusSummaries(),
            new StubEmailThreadReader([.. Exchanges.Value.SelectMany(static exchange => ThreadedOf(exchange))]),
            new CorpusContent(),
            new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions()),
            new NoRepairs(),
            scope,
            new NoDownloadLinks(),
            SensitiveContentEgressGuards.Inactive(),
            new EmailContentReadOptions(),
            new UnrecordedReads(),
            authorization);

    private static IEnumerable<(EmailThreadId ThreadId, ThreadedEmailSummary Email)> ThreadedOf(IReadOnlyList<CorpusMessage> exchange) =>
        exchange.Select((message, position) => (ThreadOf(exchange), new ThreadedEmailSummary
        {
            StoredEmailId = message.Id,
            AccountId = CorpusKnowledgeSearch.Account,
            FolderAlias = CorpusKnowledgeSearch.Inbox,
            ParentStoredEmailId = position is 0 ? null : exchange[position - 1].Id,
            Subject = message.Subject,
            SentAt = message.ReceivedAt,
            SenderAddress = message.Sender,
            SenderDisplayName = message.SenderName,
        }));

    private sealed class CorpusSummaries : IStoredEmailSummaryReader
    {
        public Task<EmailSummary?> FindAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken) =>
            Task.FromResult(Summaries.Value.GetValueOrDefault(storedEmailId));

        public Task<IReadOnlyDictionary<StoredEmailId, EmailSummary>> ReadSummariesAsync(
            IReadOnlyList<StoredEmailId> storedEmailIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<StoredEmailId, EmailSummary>>(
                storedEmailIds.Where(Summaries.Value.ContainsKey).Distinct().ToDictionary(static id => id, static id => Summaries.Value[id]));
    }

    private sealed class CorpusContent : IEmailContentStore
    {
        private static readonly Lazy<IReadOnlyDictionary<StoredEmailId, StoredEmailContent>> Stored = new(static () =>
            Exchanges.Value
                .SelectMany(static exchange => exchange)
                .ToDictionary(
                    static message => message.Id,
                    static message => new StoredEmailContent(message.RawMime, message.RawMime.Length, SHA256.HashData(message.RawMime.Span))));

        public Task<StoredEmailContent?> FindStoredContentAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken) =>
            Task.FromResult(Stored.Value.GetValueOrDefault(storedEmailId));

        public Task<PlacedEmailContent> PlaceContentAsync(EmailContentKind kind, ReadOnlyMemory<byte> rawMime, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Agent reads mail and never stores it.");

        public Task SaveContentAsync(IPersistenceSession session, StoredEmailId storedEmailId, EmailOccurrenceId? occurrenceId, PlacedEmailContent placedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Agent reads mail and never stores it.");

        public Task SaveOutgoingContentAsync(IPersistenceSession session, OutgoingEmailId outgoingEmailId, PlacedEmailContent placedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Agent proposes mail and never stores it.");

        public Task<StoredEmailContent?> FindOutgoingContentAsync(OutgoingEmailId outgoingEmailId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Agent never reads an outgoing message.");

        public Task SaveRecurringSendDraftAsync(IPersistenceSession session, RecurringSendId recurringSendId, PlacedEmailContent placedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Agent never declares a repeated send.");

        public Task<StoredEmailContent?> FindRecurringSendDraftAsync(RecurringSendId recurringSendId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Agent never reads a repeated send.");

        public Task SaveMailDraftContentAsync(IPersistenceSession session, MailDraftId draftId, PlacedEmailContent placedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Agent proposes mail and never stores a draft.");

        public Task<StoredEmailContent?> FindMailDraftContentAsync(MailDraftId draftId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Agent never reads a draft.");
    }

    private sealed class NoRepairs : IEmailContentRepairRequestStore
    {
        public Task RecordAsync(EmailContentRepairRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Every corpus message is stored intact, so nothing asks for a repair.");
    }

    private sealed class NoDownloadLinks : IAttachmentDownloadLinkIssuer
    {
        public bool CanIssueLinks => false;

        public Task<IReadOnlyList<AttachmentDownloadLink>> IssueAsync(StoredEmailId storedEmailId, int attachmentCount, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Agent never asks for a download link.");
    }

    private sealed class UnrecordedReads : IMailboxReadTelemetry
    {
        public IMailboxReadScope BeginRead(MailboxReadOperation operation, CancellationToken cancellationToken) => new Unrecorded();

        public IMailboxReadScope BeginSearchRanking(CancellationToken cancellationToken) => new Unrecorded();

        private sealed class Unrecorded : IMailboxReadScope
        {
            public void Completed(int resultCount)
            {
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class NoAttachmentContent : IEmailAttachmentContentReader
    {
        public Task<OpenedEmailAttachmentResult> OpenAsync(StoredEmailContent content, int attachmentPosition, CancellationToken cancellationToken) =>
            throw new NotSupportedException("No case forwards a message carrying an attachment.");

        public Task<OpenedEmailAttachmentWalkResult> OpenWalkAsync(StoredEmailContent content, CancellationToken cancellationToken) =>
            throw new NotSupportedException("No case forwards a message carrying an attachment.");
    }

    private sealed class OwnAddress : IOutgoingSenderIdentityReader
    {
        public OutgoingSenderIdentity? FindSenderIdentity(MailAccountId accountId) =>
            accountId == CorpusKnowledgeSearch.Account && EmailAddress.TryCreate(null, "owner@example.test", out var owner)
                ? OutgoingSenderIdentity.Create(accountId, owner)
                : null;
    }

    private sealed class NoSessions : IPersistenceSessionFactory
    {
        public Task<IPersistenceSession> BeginSessionAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Agent reads the calendar and never writes to it.");
    }

    private sealed class NoContacts : IContactDirectory
    {
        public Task<Contact?> FindAsync(ContactBookScope scope, ContactId contactId, CancellationToken cancellationToken) =>
            Task.FromResult<Contact?>(null);

        public Task<IReadOnlyDictionary<ContactId, Contact>> FindAllAsync(ContactBookScope scope, IReadOnlyCollection<ContactId> contactIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<ContactId, Contact>>(new Dictionary<ContactId, Contact>());

        public Task<Contact?> FindByAddressAsync(ContactBookScope scope, EmailAddress address, CancellationToken cancellationToken) =>
            Task.FromResult<Contact?>(null);

        public Task<IReadOnlyDictionary<ContactDisplayName, ContactMatch>> MatchDisplayNamesAsync(ContactBookScope scope, IReadOnlyCollection<ContactDisplayName> displayNames, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<ContactDisplayName, ContactMatch>>(new Dictionary<ContactDisplayName, ContactMatch>());

        public Task<IReadOnlyDictionary<EmailAddress, ContactId>> FindHoldersOfAsync(ContactBookHolder holder, IReadOnlyCollection<EmailAddress> addresses, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<EmailAddress, ContactId>>(new Dictionary<EmailAddress, ContactId>());

        public Task<IReadOnlySet<EmailAddress>> FindHeldAddressesAsync(ContactBookScope scope, IReadOnlyCollection<EmailAddress> addresses, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<EmailAddress>>(new HashSet<EmailAddress>());

        public Task<ContactPage> ReadPageAsync(ContactBookScope scope, ContactQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Agent never lists the contact book.");
    }
}
