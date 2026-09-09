// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Keeps the mailbox's copies of one account's outgoing mail in step with what the outbox is doing.</summary>
/// <remarks>
/// <para>
/// Two moments, and they are the two an outbox pass already reaches. Before it claims anything it mirrors the sends
/// that are waiting, so a message held until Monday is visible in the mail client the user actually uses rather than
/// only to somebody running a command; after a send has settled it withdraws that mirror and, where the account asked
/// for one, files the sent copy.
/// </para>
/// <para>
/// The outgoing record stays the truth about what will be sent, and neither of these changes that. A copy in a folder
/// is a view of the record: deleting it in a mail client cancels nothing, and cancelling is a command of its own. That
/// is what makes the mirror safe to leave off by default and safe to switch on — the folder never becomes a second
/// place the send is decided.
/// </para>
/// <para>
/// A deployment that has mapped no folder to the outbox role does none of this, which is every deployment that says
/// nothing. The mapping is read from configuration and costs no query, so an account without one spends nothing per
/// pass rather than reading its outbox to discover it has nothing to mirror.
/// </para>
/// </remarks>
public sealed class OutgoingMailFilingPass
{
    /// <summary>
    /// How long after an append a second occurrence of the message is still read as the provider having filed its own.
    /// </summary>
    /// <remarks>
    /// It bounds a race between one append and the synchronization run that meets what the provider filed, which is
    /// minutes on any account being synchronized at all. Past it the copy has been in the user's folder long enough to
    /// have been read, replied from, moved, or flagged, and a duplicate discovered then is more likely to be the user's
    /// own act than the provider's — so both copies stand and the one the user does not want is the one they delete. It
    /// is a constant rather than a setting because it measures how long this system waits to recognize its own copy,
    /// which is not a quantity an operator has grounds to tune.
    /// </remarks>
    private static readonly TimeSpan DuplicateSentCopyWindow = TimeSpan.FromMinutes(15);

    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly OutgoingMailFiler filer;
    private readonly IMailFolderMappingReader folderMappings;
    private readonly IOutgoingMailFilingPolicyReader filingPolicies;
    private readonly IOutgoingMailFilingStore filings;
    private readonly TimeProvider timeProvider;
    private readonly int maxMirroredSendsPerPass;

    /// <summary>Initializes the pass from the outbox it reads and the filer it acts through.</summary>
    /// <param name="outgoingEmails">Holds the records whose copies this pass keeps in step.</param>
    /// <param name="filer">Appends and withdraws one copy.</param>
    /// <param name="folderMappings">Answers whether this account maps a folder to the outbox role at all.</param>
    /// <param name="filingPolicies">Answers whether this account files a copy of what it sends.</param>
    /// <param name="filings">Answers which filed sent copies the folder holds a second occurrence of.</param>
    /// <param name="timeProvider">Decides which sends are waiting rather than merely queued.</param>
    /// <param name="settings">Bounds how many waiting sends one pass mirrors.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    /// <remarks>
    /// The mirror is bounded by the same number as the deliveries, because both are one conversation with a server per
    /// send and what one pass leaves the next one takes. A second bound would be a second number to keep in step for no
    /// difference an operator could observe.
    /// </remarks>
    public OutgoingMailFilingPass(
        IOutgoingEmailStore outgoingEmails,
        OutgoingMailFiler filer,
        IMailFolderMappingReader folderMappings,
        IOutgoingMailFilingPolicyReader filingPolicies,
        IOutgoingMailFilingStore filings,
        TimeProvider timeProvider,
        MailOutboxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(outgoingEmails);
        ArgumentNullException.ThrowIfNull(filer);
        ArgumentNullException.ThrowIfNull(folderMappings);
        ArgumentNullException.ThrowIfNull(filingPolicies);
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(settings);

        this.outgoingEmails = outgoingEmails;
        this.filer = filer;
        this.folderMappings = folderMappings;
        this.filingPolicies = filingPolicies;
        this.filings = filings;
        this.timeProvider = timeProvider;
        this.maxMirroredSendsPerPass = settings.MaxDeliveriesPerPass;
    }

    /// <summary>Puts a copy of each waiting send into the folder the account mapped as its outbox.</summary>
    /// <param name="account">The account whose waiting sends are mirrored.</param>
    /// <param name="cancellationToken">Cancels the reads and the appends.</param>
    /// <returns>What each attempt did, which is empty on an account that maps no outbox folder.</returns>
    /// <remarks>
    /// Only a send whose next attempt lies ahead is mirrored. A message the very next claim will take is gone in
    /// seconds, and appending a copy of it would put a message on somebody's mail server and take it away again for
    /// every send this deployment makes.
    /// </remarks>
    public async Task<IReadOnlyList<OutgoingMailFilingResult>> MirrorWaitingSendsAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        if (!this.MapsOutboxFolder(account.Id))
        {
            return [];
        }

        var outstanding = await this.outgoingEmails.ReadOutstandingAsync(
            account,
            this.maxMirroredSendsPerPass,
            cancellationToken);

        var asOf = this.timeProvider.GetUtcNow();
        var waiting = outstanding
            .Where(record => record.IsWaitingAt(asOf) && record.FindFiling(OutgoingMailFiling.Held) is null)
            .ToArray();

        var results = new List<OutgoingMailFilingResult>(waiting.Length);
        foreach (var record in waiting)
        {
            results.Add(await this.filer.FileAsync(record, OutgoingMailFiling.Held, cancellationToken));
        }

        return results;
    }

    /// <summary>Takes back the sent copies whose provider has since filed one of its own beside them.</summary>
    /// <param name="account">The account whose sent folder is brought back to one copy of each message.</param>
    /// <param name="cancellationToken">Cancels the read and the withdrawals.</param>
    /// <returns>What each withdrawal did, which is empty on an account that has asked for none.</returns>
    /// <remarks>
    /// <para>
    /// A provider that files the sent copy itself does it asynchronously, so nothing before the send can tell whether it
    /// will. What can be told, once synchronization has met a second occurrence of the message in the same folder, is
    /// that the folder now holds two — and the one this system appended is the one it may take back out.
    /// </para>
    /// <para>
    /// It runs on the outbox pass rather than on the synchronization run that discovers the duplicate, because taking a
    /// message out of a folder is a write and no read path may obtain the session that makes one. The cost is one query
    /// per pass on an account whose sent folder holds no duplicate, which is every account whose provider files
    /// nothing.
    /// </para>
    /// <para>
    /// One pass withdraws no more copies than it delivers sends, for the reason the mirror is bounded by that same
    /// number: each is one conversation with a server, and what one pass leaves the next one takes. A record an erasure
    /// removed between the read and the withdrawal is passed over rather than reported, because the copy it named
    /// belongs to a message this deployment no longer holds.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<OutgoingMailFilingResult>> WithdrawDuplicatedSentCopiesAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        if (!this.filingPolicies.WithdrawsDuplicateSentCopy(account.Id))
        {
            return [];
        }

        var duplicated = await this.filings.ReadDuplicatedSentCopiesAsync(
            account,
            this.timeProvider.GetUtcNow() - DuplicateSentCopyWindow,
            this.maxMirroredSendsPerPass,
            cancellationToken);

        var results = new List<OutgoingMailFilingResult>(duplicated.Count);
        foreach (var outgoingEmailId in duplicated)
        {
            if (await this.outgoingEmails.FindAsync(outgoingEmailId, cancellationToken) is not { } record)
            {
                continue;
            }

            results.Add(await this.filer.WithdrawAsync(record, OutgoingMailFiling.Sent, cancellationToken));
        }

        return results;
    }

    /// <summary>Brings the copies of one settled send up to date: the mirror goes, and the sent copy arrives.</summary>
    /// <param name="outgoingEmailId">The send whose attempt has just ended.</param>
    /// <param name="cancellationToken">Cancels the reads and the appends.</param>
    /// <returns>What each attempt did, which is empty for a send that is still queued.</returns>
    /// <remarks>
    /// <para>
    /// The record is read again rather than taken from the attempt, because the attempt handed back what it decided and
    /// this needs what was committed — including the filing rows an earlier pass wrote, which no delivery result
    /// carries.
    /// </para>
    /// <para>
    /// The order is deliberate. The mirror is withdrawn first, so a folder the user is looking at never shows the same
    /// message as both waiting and sent; the sent copy is appended only after a delivery the server acknowledged, which
    /// is the one point at which saying <em>this was sent</em> is true.
    /// </para>
    /// <para>
    /// It is keyed on the send having reached a terminal stage rather than on how it got there, so every path that
    /// settles one calls this and none of them needs its own withdrawal. A send that ends without an attempt has the
    /// mirror taken out of the folder and nothing appended in its place, because the only copy such a message ever
    /// earned was the one that said it was waiting.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<OutgoingMailFilingResult>> SettleFiledCopiesAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        if (await this.outgoingEmails.FindAsync(outgoingEmailId, cancellationToken) is not { IsTerminal: true } record)
        {
            return [];
        }

        var results = new List<OutgoingMailFilingResult>(capacity: 2);

        if (record.FindFiling(OutgoingMailFiling.Held) is { IsStanding: true })
        {
            results.Add(await this.filer.WithdrawAsync(record, OutgoingMailFiling.Held, cancellationToken));
        }

        if (record.Stage != OutgoingEmailStage.Sent)
        {
            return results;
        }

        if (!this.filingPolicies.FilesSentCopy(record.AccountId))
        {
            results.Add(new OutgoingMailFilingResult(
                record.Id,
                OutgoingMailFiling.Sent,
                OutgoingMailFilingOutcome.NotRequested,
                Failure: null));

            return results;
        }

        results.Add(await this.filer.FileAsync(record, OutgoingMailFiling.Sent, cancellationToken));

        return results;
    }

    /// <summary>Reports whether this account has given a folder the outbox role at all.</summary>
    /// <remarks>
    /// It is a configuration read rather than a query, and it is what makes the mirror off by default: no folder
    /// carries this role unless an operator wrote it, and a provider folder merely named like an outbox carries
    /// nothing.
    /// </remarks>
    private bool MapsOutboxFolder(MailAccountId accountId) =>
        this.folderMappings.FindFolderPlayingRole(accountId, MailFolderSpecialUse.Outbox) is not null;
}
