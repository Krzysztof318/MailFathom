// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts.Correspondence;

/// <summary>Correlates one contact with the mail the caller may already read about them.</summary>
/// <remarks>
/// <para>
/// It stores nothing and derives nothing durable. A contact is a name and a set of addresses; what an opened contact
/// shows beside it — the exchanges those addresses appear in and the documents they sent — is a question the mail index
/// can already answer, so it is answered on the read instead of being copied onto the contact and kept in step with a
/// mailbox afterwards.
/// </para>
/// <para>
/// It answers through the visibility the caller already has. The scope is
/// <see cref="MailboxScopeResolver.ReadableScope" />'s, unchanged — the accounts the signed-in user is assigned, the
/// folders a mapping admits, no junk — so a message this caller may not see is outside the query rather than filtered
/// out of its result, and no list here carries a withheld entry for somebody to notice. Junk is left out by the same
/// default every listing takes: what an opened contact answers is what this person and the reader have been doing, and
/// a message an account's own junk folder holds is not that.
/// </para>
/// <para>
/// It reaches no mail server. Both lists answer from what synchronization has already stored and what derivation has
/// already indexed, so no request from a browser can wait on IMAP and none can set the remote <c>\Seen</c> flag.
/// </para>
/// <para>
/// It is one of the points mail content leaves this deployment, so where a sensitive-content scanner is switched on the
/// subjects and the file names are scanned before the answer is returned; a scanner that cannot answer refuses the
/// answer rather than serving it unscanned.
/// </para>
/// </remarks>
public sealed class ContactCorrespondenceReader
{
    private readonly IContactCorrespondenceIndex correspondenceIndex;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IMailboxReadTelemetry readTelemetry;
    private readonly AccessAuthorization authorization;
    private readonly TimeProvider clock;

    /// <summary>Initializes the use case.</summary>
    /// <param name="correspondenceIndex">Reads the conversations and the documents the addresses appear in.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the correlation runs against.</param>
    /// <param name="egressGuard">Scans what the answer is about to publish, where this deployment scans anything.</param>
    /// <param name="readTelemetry">Publishes the correlation as the operation it is, beside the call it happened inside.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="clock">Reads the instant the window is measured back from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public ContactCorrespondenceReader(
        IContactCorrespondenceIndex correspondenceIndex,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard,
        IMailboxReadTelemetry readTelemetry,
        AccessAuthorization authorization,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(correspondenceIndex);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(readTelemetry);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(clock);

        this.correspondenceIndex = correspondenceIndex;
        this.scopeResolver = scopeResolver;
        this.egressGuard = egressGuard;
        this.readTelemetry = readTelemetry;
        this.authorization = authorization;
        this.clock = clock;
    }

    /// <summary>Reads what the caller's own mail holds about one contact.</summary>
    /// <param name="contact">The contact, as the caller's own book answered it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The conversations and the documents, each bounded and newest first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contact" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the answer carries, which refuses the answer rather than serving it unscanned.</exception>
    /// <remarks>
    /// Nothing here writes, and the operation is therefore safe to repeat. The grant is the mail one rather than the
    /// contact one, because what this publishes is mail: reading the contact itself is the caller's own book's decision
    /// and was taken before this was reached.
    /// </remarks>
    public async Task<ContactCorrespondence> ReadAsync(Contact contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        using var read = this.readTelemetry.BeginRead(MailboxReadOperation.CorrelateContactMail, cancellationToken);

        using var actingFor = this.egressGuard.ActingFor(this.scopeResolver.User);

        var scope = this.scopeResolver.ReadableScope([], [], JunkMailInclusion.Excluded);

        // A caller assigned no account is answered before either index is reached, for the reason every mail read
        // leaves early on one: the scope admits no row, so both queries would return nothing more expensively.
        if (scope.AccountIds.Count is 0)
        {
            read.Completed(0);

            return ContactCorrespondence.Nothing;
        }

        var correspondedOnOrAfter = ContactCorrespondenceBounds.WindowStartingBefore(this.clock.GetUtcNow());
        var addresses = AddressesOf(contact);

        var threads = await this.correspondenceIndex.ReadRecentThreadsAsync(
            scope,
            addresses,
            correspondedOnOrAfter,
            cancellationToken);

        var documents = await this.correspondenceIndex.ReadRecentDocumentsAsync(
            scope,
            addresses,
            correspondedOnOrAfter,
            cancellationToken);

        var correspondence = await this.GuardedAsync(threads, documents, cancellationToken);

        read.Completed(correspondence.Threads.Count + correspondence.Documents.Count);

        return correspondence;
    }

    /// <summary>Reads the comparison forms of the contact's addresses, which is the only form the index matches on.</summary>
    private static IReadOnlyList<string> AddressesOf(Contact contact) =>
        [.. contact.Addresses.Select(static address => address.NormalizedAddress)];

    /// <summary>Scans the two things in the answer a sender wrote: a conversation's subject, and a file's name.</summary>
    /// <remarks>
    /// Everything else is what a screen acts on — the conversation, the message, the attachment's walk position, the
    /// declared type, and the instants — and none of it is text a producer composed. The subject is scanned for the
    /// reason every listing scans it, and the file name for the reason the search does: a sender chose it, and it
    /// travels beside the same message whose subject was redacted.
    /// </remarks>
    private async Task<ContactCorrespondence> GuardedAsync(
        IReadOnlyList<CorrespondingThread> threads,
        IReadOnlyList<CorrespondingDocument> documents,
        CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return new ContactCorrespondence(threads, documents);
        }

        // One report for the answer rather than one per entry, because the answer is what a screen waits for.
        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.ClientContactCorrespondence,
            cancellationToken);

        var guardedThreads = new List<CorrespondingThread>(threads.Count);

        foreach (var thread in threads)
        {
            guardedThreads.Add(thread with
            {
                Subject = await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ClientContactCorrespondence,
                    thread.Subject,
                    cancellationToken),
            });
        }

        var guardedDocuments = new List<CorrespondingDocument>(documents.Count);

        foreach (var document in documents)
        {
            guardedDocuments.Add(document with
            {
                FileName = await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ClientContactCorrespondence,
                    document.FileName,
                    cancellationToken),
            });
        }

        scan.Completed();

        return new ContactCorrespondence(guardedThreads, guardedDocuments);
    }
}
