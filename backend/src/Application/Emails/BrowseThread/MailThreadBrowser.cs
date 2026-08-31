// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseThread;

/// <summary>Reads one conversation as the document a thread screen is drawn from, out of the local mailbox copy.</summary>
/// <remarks>
/// <para>
/// It is the read beside <see cref="BrowseTimeline.MailTimelineBrowser" /> that answers what a list row's conversation
/// is. The two
/// share the ownership scoping, the page bound, and the projection a message is drawn from, and differ in what decides
/// membership: a list is a filtered walk over a folder, and a conversation is every message threading put together
/// whatever folder it landed in. That is why nothing here narrows by folder — the question is in the inbox, the answer
/// is in the sent folder, and a thread cut to one of them is half an exchange.
/// </para>
/// <para>
/// The order is <see cref="EmailThreadOrder" />'s and is deliberately not this reading's own: the reply relation
/// decides, the sent time settles messages answering the same parent, and the identity settles the rest. It is the same
/// order the conversation an MCP content read publishes has, so two surfaces cannot come to disagree about one exchange,
/// and for the ordinary exchange where every message answers the one before it that order is chronological.
/// </para>
/// <para>
/// What a message carries is what a collapsed row draws — the listing's own projection, plus the bounded opening of what
/// this message added with the quoted history trimmed off. The whole message, quoted history included, is reached by the
/// identity that row carries; no body crosses this boundary, because a conversation of fifty messages carrying bodies is
/// a megabyte to draw a thread.
/// </para>
/// <para>
/// It reaches no mail server. A conversation is answered from what synchronization has already stored, so no request
/// from a browser can wait on IMAP and none can set the remote <c>\Seen</c> flag.
/// </para>
/// <para>
/// A page is one of the points mail content leaves this deployment, so where a sensitive-content scanner is switched on
/// the subject, the sender's display name and the contribution of every message are scanned before the page is returned,
/// and so is every participant's display name; a scanner that cannot answer refuses the page rather than serving it
/// unscanned.
/// </para>
/// </remarks>
public sealed class MailThreadBrowser
{
    private readonly IEmailThreadReader threadReader;
    private readonly IStoredEmailSummaryReader summaryReader;
    private readonly IStoredEmailPreviewReader previewReader;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IMailboxReadTelemetry readTelemetry;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the use case.</summary>
    /// <param name="threadReader">Reads which messages one conversation holds, narrowed to what the caller may see.</param>
    /// <param name="summaryReader">Reads the listing projection of the messages one page names.</param>
    /// <param name="previewReader">Reads the bounded opening of the text of those messages.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the conversation is read across.</param>
    /// <param name="egressGuard">Scans what the page is about to publish, where this deployment scans anything.</param>
    /// <param name="readTelemetry">Publishes the read as the operation it is, beside the call it happened inside.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailThreadBrowser(
        IEmailThreadReader threadReader,
        IStoredEmailSummaryReader summaryReader,
        IStoredEmailPreviewReader previewReader,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard,
        IMailboxReadTelemetry readTelemetry,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(threadReader);
        ArgumentNullException.ThrowIfNull(summaryReader);
        ArgumentNullException.ThrowIfNull(previewReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(readTelemetry);
        ArgumentNullException.ThrowIfNull(authorization);

        this.threadReader = threadReader;
        this.summaryReader = summaryReader;
        this.previewReader = previewReader;
        this.scopeResolver = scopeResolver;
        this.egressGuard = egressGuard;
        this.readTelemetry = readTelemetry;
        this.authorization = authorization;
    }

    /// <summary>Reads one page of one conversation.</summary>
    /// <param name="request">What the screen asked for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The page, or <see langword="null" /> when this caller has no conversation under that identifier.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxQueryPageSizeOutOfRangeException">Thrown when the request names a page size outside the accepted range.</exception>
    /// <exception cref="MailboxQueryCursorMalformedException">Thrown when the request carries a cursor this system did not issue.</exception>
    /// <exception cref="MailboxQueryCursorFilterMismatchException">Thrown when the cursor was issued for a different conversation than the request names.</exception>
    /// <exception cref="EmailThreadCursorMessageMissingException">Thrown when the cursor names a message the conversation no longer shows.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the page carries, which refuses the page rather than serving it unscanned.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// <para>
    /// Nothing here writes, and the operation is therefore safe to repeat. The grant is asked for before the request is
    /// validated, so a caller that may not read learns nothing about which cursors this deployment accepts.
    /// </para>
    /// <para>
    /// An identifier naming no conversation this caller may see and one naming no conversation at all answer
    /// identically, so nothing in the answer separates somebody else's exchange from one that never existed.
    /// </para>
    /// </remarks>
    public async Task<BrowsedThread?> BrowsePageAsync(
        BrowseThreadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        using var read = this.readTelemetry.BeginRead(MailboxReadOperation.ReadEmailThread, cancellationToken);

        using var actingFor = this.egressGuard.ActingFor(this.scopeResolver.Owner);

        var pageSize = MailboxQueryPageSize.FromRequested(request.PageSize);
        var fingerprint = EmailThreadCursor.FingerprintOf(request.ThreadId);
        var boundary = ContinuationBoundary(request.Cursor, fingerprint);

        // The junk folder takes part, unlike in a listing: a reply that landed in junk is part of the exchange somebody
        // is reading rather than mail they asked to be shown. Neither an account nor a folder narrows the read, because
        // a conversation is read by membership and the folder a caller opened it from would cut it.
        var scope = this.scopeResolver.ReadableScope([], [], JunkMailInclusion.Included);

        // Every value has been validated by this point, so a deployment serving this owner no account answers the same
        // refusals a deployment serving several does, and only then reports that it holds no such conversation.
        if (scope.AccountIds.Count is 0)
        {
            read.Completed(0);

            return null;
        }

        var assembled = await this.threadReader.ReadEmailsAsync(request.ThreadId, scope, cancellationToken);

        if (assembled.Count is 0)
        {
            read.Completed(0);

            return null;
        }

        var moreNotAssembled = assembled.Count > IEmailThreadReader.MaximumAssembledEmails;
        var ordered = EmailThreadOrder.Of([.. assembled.Take(IEmailThreadReader.MaximumAssembledEmails)]);
        var walked = PageOf(ordered, boundary, pageSize);

        var messages = await this.MessagesOfAsync(walked, cancellationToken);
        var participants = ParticipantsOf(ordered);

        var page = await this.GuardedAsync(messages, participants, cancellationToken);

        read.Completed(page.Messages.Count);

        return new BrowsedThread(
            request.ThreadId,
            page.Messages,
            page.Participants,
            ordered.Count,
            moreNotAssembled,
            participants.Count > BrowsedThread.MaximumNamedParticipants,
            CursorAfterThePage(ordered, walked, page.Messages, fingerprint),
            pageSize.Value);
    }

    /// <summary>Cuts the conversation to the page that follows the boundary the cursor named.</summary>
    /// <exception cref="EmailThreadCursorMessageMissingException">Thrown when the boundary names a message this conversation no longer shows.</exception>
    private static IReadOnlyList<PlacedThreadedEmail> PageOf(
        IReadOnlyList<PlacedThreadedEmail> ordered,
        StoredEmailId? boundary,
        MailboxQueryPageSize pageSize)
    {
        if (boundary is not { } continueAfter)
        {
            return [.. ordered.Take(pageSize.Value)];
        }

        // The order was derived a moment ago, so the boundary is found in it rather than assumed to sit where it sat
        // when the cursor was issued — which is the whole reason a message names the boundary instead of a position.
        var walkedTo = ordered
            .Select(static (message, index) => (message.Email.StoredEmailId, Index: index))
            .Where(placed => placed.StoredEmailId == continueAfter)
            .Select(static placed => (int?)placed.Index)
            .FirstOrDefault();

        if (walkedTo is not { } boundaryIndex)
        {
            throw new EmailThreadCursorMessageMissingException();
        }

        return [.. ordered.Skip(boundaryIndex + 1).Take(pageSize.Value)];
    }

    /// <summary>Issues the cursor that reads the page after this one, or nothing where the conversation ends here.</summary>
    /// <remarks>
    /// It names the last message the page returned, and where a page returned none — every message it walked having been
    /// deleted between the two reads — the last message it walked, so a client is never left without a way forward over
    /// a conversation that still has more of itself to give.
    /// </remarks>
    private static string? CursorAfterThePage(
        IReadOnlyList<PlacedThreadedEmail> ordered,
        IReadOnlyList<PlacedThreadedEmail> walked,
        IReadOnlyList<BrowsedThreadEmail> published,
        string fingerprint)
    {
        if (walked.Count is 0 || walked[^1].Position == ordered.Count - 1)
        {
            return null;
        }

        var boundary = published.Count is 0
            ? walked[^1].Email.StoredEmailId
            : published[^1].Email.StoredEmailId;

        return EmailThreadCursor.After(boundary, fingerprint).Encode();
    }

    /// <summary>Names everybody who wrote in the conversation, in the order they first wrote in it.</summary>
    /// <remarks>
    /// An author rather than an addressee, which is what a thread header draws. A message whose sender no reader could
    /// establish names nobody and is counted under no participant, rather than being gathered under an empty address.
    /// </remarks>
    private static IReadOnlyList<ThreadParticipant> ParticipantsOf(IReadOnlyList<PlacedThreadedEmail> ordered) =>
    [
        .. ordered
            .Select(static placed => placed.Email)
            .Where(static email => !string.IsNullOrWhiteSpace(email.SenderAddress))
            .GroupBy(static email => email.SenderAddress!, StringComparer.OrdinalIgnoreCase)
            .Select(static wrote => new ThreadParticipant(
                wrote.First().SenderAddress!,
                DisplayNameLastWritten(wrote),
                wrote.Count())),
    ];

    /// <summary>Answers the name this participant wrote most recently, or nothing where none of their messages carried one.</summary>
    /// <remarks>
    /// Recency is the message's own <see cref="ThreadedEmailSummary.SentAt" /> rather than its place in the conversation.
    /// The order the messages arrive in here is the reply relation first and the clock second, so a message deep under an
    /// early root is emitted before a later root's message that was actually written after it — reading the traversal's
    /// last name would then publish the older of the two spellings for somebody who wrote in both. A message no header
    /// dated settles nothing about recency and is taken only where nothing dated names this person at all, in which case
    /// the traversal's own last is as good an answer as there is.
    /// </remarks>
    private static string? DisplayNameLastWritten(IEnumerable<ThreadedEmailSummary> wrote)
    {
        var named = wrote.Where(static email => email.SenderDisplayName is not null).ToArray();

        var dated = named
            .Where(static email => email.SentAt is not null)
            .OrderBy(static email => email.SentAt!.Value)
            .LastOrDefault();

        return (dated ?? named.LastOrDefault())?.SenderDisplayName;
    }

    /// <summary>Reads what the page's messages are drawn from, dropping any the copy no longer holds.</summary>
    /// <remarks>
    /// A message deleted between the membership read and this one is left out of the page rather than published as an
    /// identity with nothing behind it. It is the same answer every other read gives a tombstoned message, and the
    /// counts beside the page still describe the conversation as the membership read found it.
    /// </remarks>
    private async Task<IReadOnlyList<BrowsedThreadEmail>> MessagesOfAsync(
        IReadOnlyList<PlacedThreadedEmail> walked,
        CancellationToken cancellationToken)
    {
        var identities = walked.Select(static placed => placed.Email.StoredEmailId).ToArray();

        var summaries = await this.summaryReader.ReadSummariesAsync(identities, cancellationToken);
        var contributions = await this.previewReader.ReadPreviewsAsync(identities, cancellationToken);

        return
        [
            .. walked
                .Where(placed => summaries.ContainsKey(placed.Email.StoredEmailId))
                .Select(placed => new BrowsedThreadEmail(
                    summaries[placed.Email.StoredEmailId],
                    placed.Position,
                    placed.AnsweredStoredEmailId,
                    ContributionOf(placed.Email.StoredEmailId, contributions))),
        ];
    }

    /// <summary>Reads what one message added, which is absent for a message nothing has extracted yet.</summary>
    private static string? ContributionOf(
        StoredEmailId storedEmailId,
        IReadOnlyDictionary<StoredEmailId, string> contributions) =>
        contributions.TryGetValue(storedEmailId, out var contribution) ? EmailPreview.Bounded(contribution) : null;

    /// <summary>Scans everything on the page a message's author wrote, and names the participants the list may name.</summary>
    /// <remarks>
    /// The subject, the sender's display name and the contribution are the three a list row is scanned for, and for the
    /// same reasons. A participant's display name is the fourth because it is the same value published a second way: a
    /// header naming somebody the rows beneath it had redacted would leave the two disagreeing about who wrote what.
    /// The addresses are left alone on the line every other read draws — a routing identity a caller acts on rather than
    /// free text somebody wrote.
    /// </remarks>
    private async Task<(IReadOnlyList<BrowsedThreadEmail> Messages, IReadOnlyList<ThreadParticipant> Participants)>
        GuardedAsync(
            IReadOnlyList<BrowsedThreadEmail> messages,
            IReadOnlyList<ThreadParticipant> participants,
            CancellationToken cancellationToken)
    {
        var named = participants.Take(BrowsedThread.MaximumNamedParticipants).ToArray();

        if (!this.egressGuard.IsActive)
        {
            return (messages, named);
        }

        // One report for the page rather than one per message, because the page is what a screen waits for.
        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.ClientMailListing,
            cancellationToken);

        var guardedMessages = new List<BrowsedThreadEmail>(messages.Count);

        foreach (var message in messages)
        {
            guardedMessages.Add(message with
            {
                Email = message.Email with
                {
                    Subject = await this.GuardedTextAsync(message.Email.Subject, cancellationToken),
                    SenderDisplayName = await this.GuardedTextAsync(message.Email.SenderDisplayName, cancellationToken),
                },
                Contribution = await this.GuardedTextAsync(message.Contribution, cancellationToken),
            });
        }

        var guardedParticipants = new List<ThreadParticipant>(named.Length);

        foreach (var participant in named)
        {
            guardedParticipants.Add(participant with
            {
                DisplayName = await this.GuardedTextAsync(participant.DisplayName, cancellationToken),
            });
        }

        scan.Completed();

        return (guardedMessages, guardedParticipants);
    }

    /// <summary>Scans one value this page would publish, at the point the whole page is scanned under.</summary>
    private Task<string?> GuardedTextAsync(string? text, CancellationToken cancellationToken) =>
        this.egressGuard.GuardOptionalAsync(
            SensitiveContentEgressPoint.ClientMailListing,
            text,
            cancellationToken);

    /// <summary>Reads the message a cursor continues after, after establishing that it belongs to this conversation.</summary>
    /// <remarks>
    /// A blank cursor is the start of the conversation rather than a malformed one, for the reason the timeline reads it
    /// that way: a client carrying the field with nothing in it yet has asked for the beginning of the walk.
    /// </remarks>
    private static StoredEmailId? ContinuationBoundary(string? cursor, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        if (!EmailThreadCursor.TryDecode(cursor, out var decodedCursor))
        {
            throw new MailboxQueryCursorMalformedException();
        }

        if (!string.Equals(decodedCursor.ThreadFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new MailboxQueryCursorFilterMismatchException();
        }

        return decodedCursor.StoredEmailId;
    }
}
