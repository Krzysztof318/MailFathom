// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;

namespace MailFathom.Application.Emails.Extraction;

/// <summary>Carries the normalized metadata read out of one message's raw MIME.</summary>
/// <param name="OccurrenceId">The stable remote occurrence the metadata was read from.</param>
/// <param name="Subject">The decoded subject, or <see langword="null" /> when the message carried none.</param>
/// <param name="SentAt">The <c>Date</c> header in UTC, or <see langword="null" /> when the message carried none or wrote an unparseable one.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message in UTC, or <see langword="null" /> when no <c>Received</c> header carried a usable date.</param>
/// <param name="Participants">Every usable address the message wrote, each paired with the header it appeared in.</param>
/// <param name="ThreadReferences">The identifiers that place the message in a conversation.</param>
/// <param name="Attachments">What the message carries besides its body.</param>
/// <param name="Text">The searchable text the body yielded, or the reason it yielded none.</param>
/// <param name="SenderAuthentication">What the receiving server established about who sent the message.</param>
/// <remarks>
/// Participants, subject, thread identifiers, body text, and every domain the sender-authentication verdict names are
/// personal data by default. Nothing in this record may be written to a log; only counts, the occurrence identity, and
/// the verdict's own outcome are safe to report.
/// </remarks>
public sealed record ExtractedEmailMetadata(
    EmailOccurrenceId OccurrenceId,
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<EmailParticipant> Participants,
    EmailThreadReferences ThreadReferences,
    EmailAttachmentSummary Attachments,
    ExtractedEmailText Text,
    SenderAuthentication SenderAuthentication)
{
    /// <summary>Gets what this deployment made of the message's authenticated author.</summary>
    /// <remarks>
    /// Not a parameter of the reading, because it is not read out of the message: the parsing adapter establishes what
    /// the receiving server said and <see cref="SenderTrustEvaluatingEmailMimeReader" /> holds the author that follows
    /// from it against the account's trusted senders afterwards. Its default is therefore the verdict of a reading no
    /// policy has spoken over, which is unknown and carries no policy revision to say otherwise.
    /// </remarks>
    public SenderTrust SenderTrust { get; init; } = SenderTrust.NotEvaluated;

    /// <summary>Gets what the message said about having been sent by a machine rather than written to one person.</summary>
    /// <remarks>
    /// Read out of the message like every other member here, and deliberately not stored: it is a claim the sender made
    /// about one delivery, and the only thing that reads it — collection into the contact book — runs inside the pass
    /// that parsed the message. A column for it would be a column nothing queries, and a re-derivation that had to
    /// answer for it would be re-reading the same headers this reading already has in hand.
    /// </remarks>
    public EmailAutomation Automation { get; init; } = EmailAutomation.None;

    /// <summary>Gets how much this message's own text reads as machine written.</summary>
    /// <remarks>
    /// Not a parameter of the reading, for the reason <see cref="SenderTrust" /> is not: the parsing adapter produces
    /// the text and <see cref="MachineAuthorshipEvaluatingEmailMimeReader" /> judges it afterwards, against a weighting
    /// this deployment owns. Its default is therefore the state of a message nothing assessed, which is what a
    /// deployment that assesses no authorship stores and what a message with no readable body carries.
    /// </remarks>
    public MachineAuthorshipAssessment MachineAuthorship { get; init; } = MachineAuthorshipAssessment.NotAssessed;

    /// <summary>Gets the owner's scanning posture this reading was redacted under, or nothing where none redacted it.</summary>
    /// <remarks>
    /// Carried on the reading rather than resolved where the reading is written, because the two happen at different
    /// moments and a posture can change between them: a batch of mail is read outside any transaction and commits
    /// afterwards, so a stamp taken at the write would record a configuration the text beside it never went through —
    /// and a row stamped with a posture stricter than the one that produced it is a row nothing ever revisits. The
    /// value is set by <see cref="RedactingEmailMimeReader" /> for the same reason
    /// <see cref="SenderTrust" /> is set by the reader that judges it, and is <see langword="null" /> exactly where
    /// nothing scans this owner's mail.
    /// </remarks>
    public SensitiveContentDerivationStamp? RedactedUnder { get; init; }
}
