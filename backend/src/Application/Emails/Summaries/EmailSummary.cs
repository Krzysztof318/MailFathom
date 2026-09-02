// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.Summaries;

/// <summary>Describes one stored email as a mailbox listing shows it, without any of its content.</summary>
/// <remarks>
/// <para>
/// The summary is the bounded projection a list operation returns, which is the data-minimization control the privacy
/// design names for listing mail: it carries no raw MIME, no body, and no attachment bytes, and no query that produces
/// it reads the columns that hold them. What it does carry is personal data — a subject, a sender, and the addressees —
/// and inherits the classification of the mail it summarizes.
/// </para>
/// <para>
/// The addressees are the <c>To</c> addresses only. <c>Cc</c> and <c>Reply-To</c> are stored and filterable but not
/// listed, because a listing exists to let a reader recognize a message and the full participant set belongs to reading
/// it. Display names are absent for the same reason they are not persisted beside the sender's.
/// </para>
/// </remarks>
public sealed record EmailSummary
{
    /// <summary>Gets the stable local identity of the email, which every later request names it by.</summary>
    public required StoredEmailId StoredEmailId { get; init; }

    /// <summary>Gets the account whose mailbox the email was read from, named by its owner and its identifier.</summary>
    /// <remarks>
    /// The pair, read back from the email's own row: an identifier names one account within its owner, and a summary
    /// that feeds a write — an authored answer, a draft — supplies the owner that write records without asking the
    /// account table again.
    /// </remarks>
    public required MailAccountIdentity Account { get; init; }

    /// <summary>Gets the identifier half of <see cref="Account" />, which is what code already narrowed to one owner names.</summary>
    /// <remarks>
    /// Derived rather than stored, so the pair is the one value here and the two halves can never disagree. It is kept
    /// because most readers of this record are inside a scope whose owner is already settled, and naming the identifier
    /// alone there says what the code means.
    /// </remarks>
    public MailAccountId AccountId => this.Account.Id;


    /// <summary>Gets the folder alias the email was read from, which is MailFathom's own name for that folder.</summary>
    public required MailFolderAlias FolderAlias { get; init; }

    /// <summary>Gets the conversation the email belongs to, or <see langword="null" /> when nothing has placed it in one.</summary>
    /// <remarks>
    /// It is what lets a reader tell a reply from an unrelated message carrying the same subject without fetching
    /// either, and it is what a content read names to ask for the whole conversation. It is absent on mail stored before
    /// this deployment assembled threads at all, until <c>mfctl mailbox rederive</c> reaches that mail.
    /// </remarks>
    public EmailThreadId? ThreadId { get; init; }

    /// <summary>Gets the <c>Message-ID</c> the message carried, or <see langword="null" /> when it carried none this reader accepted.</summary>
    public string? InternetMessageId { get; init; }

    /// <summary>Gets the subject, or <see langword="null" /> when the message carried none.</summary>
    public string? Subject { get; init; }

    /// <summary>Gets when the message says it was sent, or <see langword="null" /> when no header carried a usable date.</summary>
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>Gets when the last receiving hop recorded the message, or <see langword="null" /> when no header carried a usable date.</summary>
    /// <remarks>This is the timeline's ordering column, and an email that has none sorts at the undated end of the direction being read.</remarks>
    public DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>Gets the size the mail server reported for the message.</summary>
    public long SizeOctets { get; init; }

    /// <summary>Gets the display name the sender wrote, or <see langword="null" /> when the header carried none.</summary>
    public string? SenderDisplayName { get; init; }

    /// <summary>Gets the sender's address as the message wrote it, or <see langword="null" /> when no usable sender was found.</summary>
    public string? SenderAddress { get; init; }

    /// <summary>Gets the comparison forms of the <c>To</c> addresses, in header order.</summary>
    public IReadOnlyList<string> ToAddresses { get; init; } = [];

    /// <summary>Gets what was established about the author the message displays, and what this deployment made of it.</summary>
    /// <remarks>
    /// Carried by the summary rather than read separately because it is what a reader weighs a listed message by, and
    /// because the single-email read is built from this same summary — so a listing and a read cannot disagree about
    /// one message.
    /// </remarks>
    public required SenderVerification SenderVerification { get; init; }

    /// <summary>Gets what the verdict above was reached from, which only a single-email read publishes.</summary>
    /// <remarks>
    /// Projected with the summary rather than fetched by a second query, because one projection is what keeps every
    /// read path publishing the same columns. A listing carries it without publishing it: a listing exists to let a
    /// reader recognize a message, and the evidence is for judging one already found.
    /// </remarks>
    public required SenderAuthenticationEvidence SenderAuthenticationEvidence { get; init; }

    /// <summary>Gets how much the email's own text read as machine written, as extraction assessed it.</summary>
    /// <remarks>
    /// Carried by the summary for the reason the sender verdicts are: it is something a reader weighs a listed message
    /// by, and the single-email read is built from this same summary — so a listing and a read cannot disagree about
    /// one message. It is an informational reading of the text and never a finding against the message; what the
    /// listing publishes of it is the band and the number, and the signals behind them belong to the read of a message
    /// somebody has already found.
    /// </remarks>
    public required MachineAuthorshipAssessment MachineAuthorship { get; init; }

    /// <summary>Gets what the email carries besides its body.</summary>
    public required StoredEmailAttachmentSummary Attachments { get; init; }

    /// <summary>Gets whether the raw MIME of the email is stored locally, or why it is not.</summary>
    /// <remarks>A caller reads this before asking for content, because a listing is served from local state whether or not a mail server is reachable.</remarks>
    public required StoredEmailContentAvailability ContentAvailability { get; init; }

    /// <summary>Gets the flags a mail server last showed for the email, and when they were read.</summary>
    public required RemoteEmailFlagSnapshot RemoteFlags { get; init; }

    /// <summary>Gets where the email sits in the timeline order, which is the boundary a continuation cursor is built from.</summary>
    public EmailTimelinePosition Position => new(this.ReceivedAt, this.StoredEmailId);
}
