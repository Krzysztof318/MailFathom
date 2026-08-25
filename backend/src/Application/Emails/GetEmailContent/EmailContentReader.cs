// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.ObjectModel;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>Reads the content of the emails one call names, from the local mailbox copy.</summary>
/// <remarks>
/// <para>
/// The use case owns the whole path from identifiers to readable messages: it establishes that each email belongs to an
/// account this deployment serves, decides whether content exists to read at all, verifies that what is stored is what
/// was written, has it rendered, and turns a damaged local copy into a stable per-email failure and a durable repair
/// request.
/// </para>
/// <para>
/// A read names several emails and answers for each of them separately, because one identifier this deployment cannot
/// serve must not discard the content of the others. What it refuses outright is the request rather than an email: a
/// list naming nothing, a list longer than one call serves, and a list naming the same email twice are all decided
/// before anything is read.
/// </para>
/// <para>
/// Two bounds stand between a caller and a mailbox, and both are the deployment's rather than the request's: each body
/// representation is bounded on its own, and the call as a whole has a character budget spent in the order the emails
/// were named. Everything they cut says which bound cut it, so a caller can tell a message worth reading alone from a
/// batch worth splitting. Attachments are subject to neither, because no octet of one is ever returned here.
/// </para>
/// <para>
/// What a read hands back for a file is a short-lived link to fetch it, minted only where the request asked for one and
/// only where the deployment is configured to issue any. The link is a bearer capability, so minting it is an act rather
/// than a projection: it is the one thing this use case produces that did not already exist in the mailbox.
/// </para>
/// <para>
/// It reaches no mail server. That is the acceptance criterion this operation exists under rather than a property it
/// happens to have: reading mail must never download it and must never set the remote <c>\Seen</c> flag, so a missing
/// local copy is answered with a failure and a repair request instead of a fetch. The use case holds no mailbox port,
/// which is what makes the guarantee structural rather than a rule someone has to keep.
/// </para>
/// <para>
/// Where a scanner is switched on, what the message's author wrote is scanned on the way out of every read and returned
/// with its findings replaced. Nothing stored is rewritten by that — not the raw MIME and not the extracted text — so
/// the redaction is paid for per call and the local copy stays the artifact it was fetched as. It is the same redaction
/// the derived path applies, which is what makes a citation drawn from a redacted chunk land on the same redacted text
/// when a reader opens the message.
/// </para>
/// </remarks>
public sealed class EmailContentReader
{
    /// <summary>How many of one message's display names a scanned read analyzes before it publishes addresses alone.</summary>
    /// <remarks>
    /// Set where a real message stops and a list expansion begins: correspondence a person reads names a handful of
    /// people, a thread across two departments names a few dozen, and an addressee list beyond that is a distribution
    /// somebody expanded rather than a set of names anybody reads. Both sides of the bound cost something, and the
    /// cheaper one is losing a display name past the fortieth participant of one message.
    /// </remarks>
    private const int MaximumScannedDisplayNames = 40;

    private readonly IStoredEmailSummaryReader summaryReader;
    private readonly IEmailThreadReader threadReader;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailContentRenderer renderer;
    private readonly IEmailContentRepairRequestStore repairRequestStore;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IAttachmentDownloadLinkIssuer linkIssuer;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly EmailContentReadOptions readOptions;
    private readonly IMailboxReadTelemetry readTelemetry;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the use case.</summary>
    /// <param name="summaryReader">Reads one stored email's summary by its identity.</param>
    /// <param name="threadReader">Reads the messages of the conversation an email belongs to.</param>
    /// <param name="contentStore">Reads the raw MIME stored for an email, with what was recorded about it.</param>
    /// <param name="renderer">Turns stored raw MIME into headers, a body, and a description of every attachment.</param>
    /// <param name="repairRequestStore">Records durably that a local copy has to be fetched or read again.</param>
    /// <param name="scopeResolver">Answers whether a tool may read the mailbox an email was stored from.</param>
    /// <param name="linkIssuer">Mints the short-lived capability a caller fetches an attachment with.</param>
    /// <param name="egressGuard">Scans what the message's author wrote before a read publishes it.</param>
    /// <param name="readOptions">The bounds on how much body text one call returns.</param>
    /// <param name="readTelemetry">Publishes the read as the operation it is, beside the call it happened inside.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmailContentReader(
        IStoredEmailSummaryReader summaryReader,
        IEmailThreadReader threadReader,
        IEmailContentStore contentStore,
        IEmailContentRenderer renderer,
        IEmailContentRepairRequestStore repairRequestStore,
        MailboxScopeResolver scopeResolver,
        IAttachmentDownloadLinkIssuer linkIssuer,
        SensitiveContentEgressGuard egressGuard,
        EmailContentReadOptions readOptions,
        IMailboxReadTelemetry readTelemetry,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(summaryReader);
        ArgumentNullException.ThrowIfNull(threadReader);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(repairRequestStore);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(linkIssuer);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(readOptions);
        ArgumentNullException.ThrowIfNull(readTelemetry);
        ArgumentNullException.ThrowIfNull(authorization);

        this.summaryReader = summaryReader;
        this.threadReader = threadReader;
        this.contentStore = contentStore;
        this.renderer = renderer;
        this.repairRequestStore = repairRequestStore;
        this.scopeResolver = scopeResolver;
        this.linkIssuer = linkIssuer;
        this.egressGuard = egressGuard;
        this.readOptions = readOptions;
        this.readTelemetry = readTelemetry;
        this.authorization = authorization;
    }

    /// <summary>Reads every email the request names.</summary>
    /// <param name="request">The emails to read and which representations to produce.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One outcome per named email, in the order they were named.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what an email carries, which fails the read rather than serving it unscanned.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// <para>
    /// The grant is asked for before an email is looked up, and it is asked for here rather than only at the transport
    /// that withholds the tool, so an entrypoint added later cannot read mail by forgetting a filter.
    /// </para>
    /// <para>
    /// Reading writes nothing about the emails themselves and is safe to repeat. The one write it can perform is the
    /// repair request a damaged local copy produces, which is idempotent per email for exactly that reason.
    /// </para>
    /// <para>
    /// The emails are read one after another rather than concurrently, because the budget is carried from one to the
    /// next: what an email is allowed to return depends on what the emails before it returned, and that is what makes
    /// the bound on a whole call exact rather than approximate.
    /// </para>
    /// </remarks>
    public async Task<GetEmailContentResult> ReadContentAsync(
        GetEmailContentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        using var read = this.readTelemetry.BeginRead(MailboxReadOperation.ReadEmailContent, cancellationToken);

        // One instance per read, because a call routinely names several messages of one exchange: assembling per email
        // would read that conversation, order it, and scan its subjects once for each of them.
        var threads = new EmailThreadContexts(this.threadReader, this.scopeResolver, this.egressGuard);
        var selection = await this.SelectAsync(request, threads, cancellationToken);

        var outcomes = new List<EmailContentReadOutcome>(selection.StoredEmailIds.Count);
        var remainingCharacters = this.readOptions.MaxCharactersPerRead;

        foreach (var storedEmailId in selection.StoredEmailIds)
        {
            var outcome = await this.ReadOneAsync(
                storedEmailId,
                request,
                threads,
                remainingCharacters,
                cancellationToken);

            remainingCharacters -= CharactersReturnedBy(outcome);
            outcomes.Add(outcome);
        }

        // The emails that were served rather than the emails that were named, because the gap between the two is the
        // whole of what this read can report: a call naming ten identifiers and answering for one is a caller working
        // from a stale listing, and the count of what it asked for would say nothing about that.
        read.Completed(outcomes.Count(outcome => outcome.Content is not null));

        return new GetEmailContentResult(
            new ReadOnlyCollection<EmailContentReadOutcome>(outcomes),
            selection.Unread);
    }

    /// <summary>Resolves what the request selected into the emails this call reads, in the order it reads them.</summary>
    /// <remarks>
    /// <para>
    /// A named list is already the answer. A named conversation becomes one here, in the conversation's own order and
    /// under the same bound a caller's list is held to, so everything after this point reads one shape whichever form
    /// the caller used.
    /// </para>
    /// <para>
    /// Nothing about a conversation this deployment does not hold is reported as a failure. It resolves to no messages,
    /// exactly as a conversation whose messages all sit in folders withheld from tools does, because telling the two
    /// apart would let a caller learn which conversations exist by asking about them.
    /// </para>
    /// </remarks>
    private async Task<ReadSelection> SelectAsync(
        GetEmailContentRequest request,
        EmailThreadContexts threads,
        CancellationToken cancellationToken)
    {
        if (request.ThreadId is not { } threadId)
        {
            return new ReadSelection(request.StoredEmailIds, []);
        }

        var assembled = await threads.AssembleAsync(threadId, cancellationToken);
        var ordered = assembled.Emails.Select(placed => placed.Email.StoredEmailId).ToArray();

        return new ReadSelection(
            [.. ordered.Take(GetEmailContentRequest.MaximumEmails)],
            [.. ordered.Skip(GetEmailContentRequest.MaximumEmails)]);
    }

    /// <summary>Reads one email, or reports why it could not be.</summary>
    private async Task<EmailContentReadOutcome> ReadOneAsync(
        StoredEmailId storedEmailId,
        GetEmailContentRequest request,
        EmailThreadContexts threads,
        int remainingCharacters,
        CancellationToken cancellationToken)
    {
        var summary = await this.summaryReader.FindAsync(storedEmailId, cancellationToken);

        // An account this deployment no longer serves leaves its stored rows in place, and a folder an operator withheld
        // from tools keeps its own, so the row existing is not enough. All three cases produce one answer: telling them
        // apart would let a caller learn which identifiers exist by asking about them.
        if (summary is null || !this.scopeResolver.IsReadableByTools(summary.AccountId, summary.FolderAlias))
        {
            return EmailContentReadOutcome.NotFound(storedEmailId);
        }

        // Both states are content synchronization deliberately did not store, so neither is a damaged local copy and
        // neither schedules a repair. They are answered apart because only one of them is worth asking about again.
        if (BodyOfUnstoredContent(summary.ContentAvailability) is { } unstoredBody)
        {
            return await this.PublishAsync(
                ContentWithoutStoredMime(summary, unstoredBody),
                summary,
                threads,
                cancellationToken);
        }

        var content = await this.contentStore.FindStoredContentAsync(storedEmailId, cancellationToken);
        if (content is null)
        {
            return await this.RequestRepairAsync(summary, EmailContentDefect.Missing, cancellationToken);
        }

        if (content.FindIntegrityDefect() is { } integrityDefect)
        {
            return await this.RequestRepairAsync(summary, integrityDefect, cancellationToken);
        }

        // The object this payload was moved into could not be vouched for and the copy the database still holds answered
        // instead. The read succeeds, because refusing over bytes the deployment has would be a self-inflicted outage,
        // and the note is what makes the endpoint's failure visible while releasing that copy is still a decision an
        // operator has not yet taken. After the release the same situation is EmailContentDefect.Missing above.
        if (content.WasServedFromRetainedCopy)
        {
            await this.repairRequestStore.RecordAsync(
                new EmailContentRepairRequest(summary.StoredEmailId, EmailContentDefect.ObjectUnreadable),
                cancellationToken);
        }

        var rendering = await this.renderer.RenderAsync(
            content,
            new EmailContentRenderingBounds(
                request.IncludeSanitizedHtml,
                this.readOptions.MaxBodyCharacters,
                remainingCharacters),
            cancellationToken);

        if (rendering.Rendering is not { } rendered)
        {
            return await this.RequestRepairAsync(summary, EmailContentDefect.Unreadable, cancellationToken);
        }

        var attachments = await this.DescribeAttachmentsAsync(
            summary.StoredEmailId,
            rendered.Attachments,
            request.IncludeAttachmentDownloadLinks,
            cancellationToken);

        return await this.PublishAsync(
            ContentFrom(summary, rendered, attachments),
            summary,
            threads,
            cancellationToken);
    }

    /// <summary>Places the email in its conversation and scans what the author wrote, in that order.</summary>
    /// <remarks>
    /// The conversation is attached before the scan rather than after it because it arrives already scanned: its
    /// subjects were guarded once per conversation while it was assembled, so a read naming several messages of one
    /// exchange pays for that once instead of once per message.
    /// </remarks>
    private async Task<EmailContentReadOutcome> PublishAsync(
        ReadEmailContent content,
        EmailSummary summary,
        EmailThreadContexts threads,
        CancellationToken cancellationToken)
    {
        var placed = content with
        {
            Thread = await threads.ContextForAsync(summary.ThreadId, summary.StoredEmailId, cancellationToken),
        };

        return EmailContentReadOutcome.Read(await this.GuardedAsync(placed, cancellationToken));
    }

    /// <summary>Scans what the message's author wrote, before any of it becomes a caller's.</summary>
    /// <remarks>
    /// <para>
    /// The body is the reason this exists — it is the whole of what a message says and the one place a credential is
    /// actually pasted — and the subject and the display names are here because the listing and the search already scan
    /// theirs. A tool that returned the subject a listing had redacted would leave the two disagreeing about what the
    /// same message says, which is exactly what a caller cannot resolve on its own.
    /// </para>
    /// <para>
    /// Everything else is left as it was read. The account, the folder alias, the stored identity, the addresses, the
    /// sizes, the flags, and every attachment's file name are what a caller acts on rather than text to read, and
    /// redacting the address a reply has to go to would remove the read's whole use while protecting nothing the body
    /// did not already carry. The line falls between a routing identity and free text, which is why a display name is
    /// scanned and the address behind it is not.
    /// </para>
    /// <para>
    /// Each value is scanned on its own and the message is composed afterwards, because a scan of the composed thing
    /// could report a region covering the end of one field and the start of the next. The cost is one scan per value
    /// per read, paid on every call and stored nowhere: keeping a map of where a message's credentials sit would be a
    /// new artifact pointing straight at them, which a cheaper read does not justify.
    /// </para>
    /// <para>
    /// The conversation is not scanned here and does not need to be. Its subjects were guarded once, per conversation,
    /// while it was assembled — which is what keeps a call naming ten messages of one exchange from scanning the same
    /// fifty subjects ten times.
    /// </para>
    /// </remarks>
    private async Task<ReadEmailContent> GuardedAsync(ReadEmailContent content, CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return content;
        }

        // One report for the message rather than one per field: a header block and every body representation of one
        // email are what a reader waits for, and a span apiece would report each as quick while the read stayed slow.
        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.McpEmailContent,
            cancellationToken);

        var guarded = content with
        {
            Headers = await this.GuardedAsync(content.Headers, cancellationToken),
            Body = await this.GuardedAsync(content.Body, cancellationToken),
        };

        scan.Completed();

        return guarded;
    }

    /// <summary>Scans the two things a header block carries that a message's author wrote.</summary>
    private async Task<EmailContentHeaders> GuardedAsync(
        EmailContentHeaders headers,
        CancellationToken cancellationToken)
    {
        return headers with
        {
            Subject = await this.egressGuard.GuardOptionalAsync(
                SensitiveContentEgressPoint.McpEmailContent,
                headers.Subject,
                cancellationToken),
            Participants = await this.GuardedAsync(headers.Participants, cancellationToken),
        };
    }

    /// <summary>Scans the display name each participant carries, leaving the address it sits in front of alone.</summary>
    /// <remarks>
    /// <para>
    /// The count is bounded here rather than left to the message, because a scan is a round trip on the deployment that
    /// runs the personal-data analyzer in a container: a parse publishes up to
    /// <see cref="EmailParticipant.MaximumPerRole" /> addresses for each header role, so a list expansion would
    /// otherwise turn one read into thousands of sequential requests taking the process-wide scan permits from every
    /// listing and answering run behind it. Past <see cref="MaximumScannedDisplayNames" /> the address is published with
    /// no display name at all, which withholds rather than serves a name nothing scanned. A participant the sender wrote
    /// no name for costs nothing and counts towards nothing.
    /// </para>
    /// <para>
    /// A participant whose guarded name cannot be put back is dropped rather than published unguarded, which is the
    /// answer this use case already gives an address it cannot use. The address itself is unchanged and was accepted
    /// when the participant was built, so nothing a scanner returns reaches that branch.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<EmailParticipant>> GuardedAsync(
        IReadOnlyList<EmailParticipant> participants,
        CancellationToken cancellationToken)
    {
        var guarded = new List<EmailParticipant>(participants.Count);
        var named = 0;

        foreach (var participant in participants)
        {
            if (participant.Address.DisplayName is not { } displayName)
            {
                guarded.Add(participant);

                continue;
            }

            var guardedName = named++ < MaximumScannedDisplayNames
                ? await this.egressGuard.GuardAsync(
                    SensitiveContentEgressPoint.McpEmailContent,
                    displayName,
                    cancellationToken)
                : null;

            if (EmailAddress.TryCreate(guardedName, participant.Address.Address, out var address))
            {
                guarded.Add(participant with { Address = address });
            }
        }

        return guarded;
    }

    /// <summary>Scans each representation of a body that could be read.</summary>
    /// <remarks>
    /// A body nothing could read carries no text in either representation, so an encrypted message and one whose
    /// content was never stored reach a scanner no more often than they reach a parser.
    /// </remarks>
    private async Task<EmailContentBody> GuardedAsync(EmailContentBody body, CancellationToken cancellationToken)
    {
        if (body.Availability is not EmailBodyAvailability.Readable)
        {
            return body;
        }

        return EmailContentBody.Readable(
            await this.GuardedAsync(body.PlainText, cancellationToken),
            body.SanitizedHtml is { } sanitizedHtml
                ? await this.GuardedAsync(sanitizedHtml, cancellationToken)
                : null);
    }

    /// <summary>Scans one representation and states the analyzed ceiling as the bound it is.</summary>
    /// <remarks>
    /// <para>
    /// The scan runs over the text this read would have returned, so every character a caller receives is one a scanner
    /// saw. What the placeholders then do to the length is left alone: replacing a short credential with a longer
    /// marker can carry the text past the bound that cut it, exactly as re-serializing sanitized markup can, and the
    /// truncation metadata is stated rather than derived from the length for that reason.
    /// </para>
    /// <para>
    /// Text beyond the analyzed ceiling is dropped rather than handed on, so the representation says so — and says it
    /// over whichever bound had cut it already, because the ceiling is where the returned text now ends. It is the one
    /// of the three a caller cannot act on: naming fewer emails returns no more of this message, and only raising
    /// <c>SensitiveContent:MaximumAnalyzedCharacters</c> does.
    /// </para>
    /// <para>
    /// That cut is the one place the balanced-markup property of the sanitized representation stops holding. The
    /// renderer keeps markup balanced by shrinking its source and sanitizing again, and a ceiling applied here cuts what
    /// the sanitizer had already serialized, so a representation carrying this truncation can end inside an element. It
    /// is preferred to the alternative: re-serializing would mean handing back text the scan never analyzed.
    /// </para>
    /// <para>
    /// The source length is untouched by all of this. It states what the message held, which redaction does not change.
    /// </para>
    /// </remarks>
    private async Task<EmailBodyRepresentation> GuardedAsync(
        EmailBodyRepresentation representation,
        CancellationToken cancellationToken)
    {
        if (representation.Text.Length == 0)
        {
            return representation;
        }

        var guarded = await this.egressGuard.GuardWithOmissionAsync(
            SensitiveContentEgressPoint.McpEmailContent,
            representation.Text,
            cancellationToken);

        return representation with
        {
            Text = guarded.Text,
            Truncation = guarded.WasCutAtAnalyzedCeiling
                ? EmailBodyTruncation.SensitiveContentScanCeiling
                : representation.Truncation,
        };
    }

    /// <summary>Pairs each described attachment with the way its content is reached, or with the reason it is not.</summary>
    /// <remarks>
    /// The links are minted for the whole message in one call, because the signing material behind them is resolved per
    /// operation and erased with it. A message carrying no attachment mints nothing at all, which keeps a read of
    /// ordinary mail from touching the key ring however the request was written.
    /// </remarks>
    private async Task<IReadOnlyList<ReadEmailAttachment>> DescribeAttachmentsAsync(
        StoredEmailId storedEmailId,
        IReadOnlyList<ExtractedEmailAttachment> attachments,
        bool includeDownloadLinks,
        CancellationToken cancellationToken)
    {
        if (!includeDownloadLinks)
        {
            return [.. attachments.Select(description =>
                new ReadEmailAttachment(description, AttachmentDownload.NotRequested))];
        }

        if (!this.linkIssuer.CanIssueLinks)
        {
            return [.. attachments.Select(description =>
                new ReadEmailAttachment(description, AttachmentDownload.Unavailable))];
        }

        if (attachments.Count == 0)
        {
            return [];
        }

        var links = await this.linkIssuer.IssueAsync(storedEmailId, attachments.Count, cancellationToken);

        // Paired by position rather than zipped, because a zip over a shorter link list would drop the descriptions
        // beyond it rather than report them without a link. The lists can differ: the issuer reads the key ring at the
        // moment it mints, and the ring is reloadable, so an operator emptying it between the check above and the call
        // here gets an empty list back — and an email answered with no attachments at all, beside counts saying it has
        // some, is the one inconsistency a caller has no way to detect.
        return
        [
            .. attachments.Select((description, position) => new ReadEmailAttachment(
                description,
                position < links.Count ? AttachmentDownload.Issued(links[position]) : AttachmentDownload.Unavailable)),
        ];
    }

    /// <summary>Counts what one outcome drew from the read's character budget.</summary>
    /// <remarks>
    /// Both representations count, because both are message content a caller received. What is counted is the text as
    /// returned rather than the length the message held, so a body the per-representation bound already cut spends only
    /// what it actually published.
    /// </remarks>
    private static int CharactersReturnedBy(EmailContentReadOutcome outcome) =>
        outcome.Content is { } content
            ? content.Body.PlainText.Text.Length + (content.Body.SanitizedHtml?.Text.Length ?? 0)
            : 0;

    /// <summary>Records the defect durably and produces the outcome to report for it.</summary>
    /// <remarks>
    /// The request is recorded before the outcome is produced, so the finding survives whether or not the caller acts on
    /// what comes back.
    /// </remarks>
    private async Task<EmailContentReadOutcome> RequestRepairAsync(
        EmailSummary summary,
        EmailContentDefect defect,
        CancellationToken cancellationToken)
    {
        await this.repairRequestStore.RecordAsync(
            new EmailContentRepairRequest(summary.StoredEmailId, defect),
            cancellationToken);

        return EmailContentReadOutcome.ContentUnavailable(summary.StoredEmailId, defect);
    }

    /// <summary>Builds the content of an email whose stored MIME was read.</summary>
    /// <remarks>
    /// The counts come from the same parse as the list rather than from the row, so the two can never disagree. They
    /// would for a message stored before extraction ran: its row records no attachments until the backfill reaches it,
    /// while the message it describes has them, and reporting the row's answer beside a list of two files would be
    /// wrong in the one direction a caller cannot check.
    /// <para>
    /// The counts and the descriptions are published whatever the caller asked for, because both are what a caller
    /// decides against: how many files there are, and whether any of them is worth fetching. Only the link is the
    /// request's to ask for, which is why the attachments arrive here already paired with what was minted for them.
    /// </para>
    /// </remarks>
    private static ReadEmailContent ContentFrom(
        EmailSummary summary,
        EmailContentRendering rendering,
        IReadOnlyList<ReadEmailAttachment> attachments)
    {
        return new ReadEmailContent
        {
            StoredEmailId = summary.StoredEmailId,
            AccountId = summary.AccountId,
            FolderAlias = summary.FolderAlias,
            SizeOctets = summary.SizeOctets,
            Headers = rendering.Headers,
            Body = rendering.BodyIsEncrypted
                ? EmailContentBody.EncryptedNotReadableLocally
                : EmailContentBody.Readable(rendering.PlainTextBody, rendering.SanitizedHtmlBody),
            AttachmentSummary = SummaryOf(rendering.AttachmentSummary),
            Attachments = attachments,
            RemoteFlags = summary.RemoteFlags,
            SenderVerification = summary.SenderVerification,
            SenderAuthenticationEvidence = summary.SenderAuthenticationEvidence,
            MachineAuthorship = summary.MachineAuthorship,
        };
    }

    private static StoredEmailAttachmentSummary SummaryOf(EmailAttachmentSummary attachments) => new(
        attachments.AttachmentCount,
        attachments.TotalSizeOctets,
        attachments.InlineResourceCount,
        attachments.IsEncrypted,
        attachments.CarriesUnverifiedSignature,
        attachments.ContainsUnexpandedTnefPart);

    /// <summary>Names the body to report for an occurrence whose content was never stored, or nothing when it was.</summary>
    /// <remarks>
    /// A row recorded as available and holding no content is a different thing entirely — that is a local copy that has
    /// gone missing, and the caller answers it with a repair request rather than with a body state.
    /// </remarks>
    private static EmailContentBody? BodyOfUnstoredContent(StoredEmailContentAvailability availability) =>
        availability switch
        {
            StoredEmailContentAvailability.ExceededSizeLimit => EmailContentBody.NotStoredExceededSizeLimit,
            StoredEmailContentAvailability.AwaitingStorageHeadroom => EmailContentBody.NotStoredAwaitingStorageHeadroom,
            _ => null,
        };

    /// <summary>Builds the content of an email whose raw MIME synchronization deliberately kept out of local storage.</summary>
    /// <remarks>
    /// Everything answerable is still answered, and nothing else is. The headers come from the columns the listing is
    /// served out of, which are narrower than a parse would produce but are what exists. Both the per-attachment list
    /// and the attachment counts are absent, because nobody has ever read this message's parts: the row carries what
    /// the server's envelope reported, and an envelope says nothing about attachments, so its zero counts are unset
    /// defaults rather than a finding. The body state says why all of it is missing, and which of the two reasons it
    /// was.
    /// <para>
    /// The empty attachment list is therefore about this message's parts never having been read rather than about it
    /// carrying no files, which the absent counts beside it state.
    /// </para>
    /// </remarks>
    private static ReadEmailContent ContentWithoutStoredMime(EmailSummary summary, EmailContentBody body)
    {
        return new ReadEmailContent
        {
            StoredEmailId = summary.StoredEmailId,
            AccountId = summary.AccountId,
            FolderAlias = summary.FolderAlias,
            SizeOctets = summary.SizeOctets,
            Headers = HeadersFrom(summary),
            Body = body,
            AttachmentSummary = null,
            Attachments = [],
            RemoteFlags = summary.RemoteFlags,
            SenderVerification = summary.SenderVerification,
            SenderAuthenticationEvidence = summary.SenderAuthenticationEvidence,
            MachineAuthorship = summary.MachineAuthorship,
        };
    }

    private static EmailContentHeaders HeadersFrom(EmailSummary summary) => new(
        summary.Subject,
        summary.SentAt,
        summary.ReceivedAt,
        ParticipantsFrom(summary),
        EmailThreadReferences.Create(summary.InternetMessageId, inReplyTo: null, references: null));

    /// <summary>Rebuilds the participants a listing row can answer for, which is the sender and the direct addressees.</summary>
    /// <remarks>
    /// The row keeps the comparison forms a filter needs, so the display names of the addressees and every
    /// <c>Cc</c>, <c>Reply-To</c>, <c>Bcc</c>, and <c>Sender</c> distinction are absent here. That is a narrower reading
    /// of the message than a parse produces, and it is the honest one: nothing local holds what was not stored.
    /// </remarks>
    private static ReadOnlyCollection<EmailParticipant> ParticipantsFrom(EmailSummary summary) =>
        new List<EmailParticipant>(
        [
            .. ParticipantOrNone(EmailAddressRole.From, summary.SenderDisplayName, summary.SenderAddress),
            .. summary.ToAddresses.SelectMany(address =>
                ParticipantOrNone(EmailAddressRole.To, displayName: null, address)),
        ]).AsReadOnly();

    private static IEnumerable<EmailParticipant> ParticipantOrNone(
        EmailAddressRole role,
        string? displayName,
        string? address) =>
        EmailAddress.TryCreate(displayName, address, out var emailAddress)
            ? [new EmailParticipant(role, emailAddress)]
            : [];

    /// <summary>The emails one read serves, and the ones a named conversation held that it did not.</summary>
    /// <param name="StoredEmailIds">The emails to read, in the order they are read and the budget is spent.</param>
    /// <param name="Unread">The named conversation's remaining messages, in its order, or empty for a named list.</param>
    private sealed record ReadSelection(
        IReadOnlyList<StoredEmailId> StoredEmailIds,
        IReadOnlyList<StoredEmailId> Unread);
}
