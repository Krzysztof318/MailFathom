// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Extraction.Images;
using MailFathom.Application.SensitiveContent.Derivation;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>What reading one message's attachments produced, and the configuration it was read under.</summary>
/// <remarks>
/// The stamp travels with the readings rather than being asked for again at the write, and it is taken before the scan
/// rather than after it — the same ordering the body's own redaction keeps, and for the same reason. A posture
/// republished while a message is being read then leaves its rows stamped with the older configuration, which reads as
/// stale and is re-derived; a stamp taken at the write would record a configuration the words never went through, and
/// the rows would never be revisited.
/// </remarks>
/// <param name="Attachments">What each attachment yielded, in walk order, which is empty for a message that yielded nothing.</param>
/// <param name="RedactedUnder">What the owner's mail was redacted under, or <see langword="null" /> where nothing scans it.</param>
/// <param name="AwaitsRepair">Whether the stored copy was missing or unparseable, so a repair request was recorded for it.</param>
/// <param name="ExtractedOctetCount">The octets a parser was handed, which is what the deployment's extraction ceiling is charged.</param>
/// <param name="ProviderDescriptionCount">The chat calls this reading made, which is what the deployment's description ceiling is charged.</param>
/// <param name="RunBudgetExhausted">Whether the account run ran out of octets mid-message, so nothing about the message may be written down.</param>
public sealed record EmailAttachmentTextDerivation(
    IReadOnlyList<DerivedAttachmentText> Attachments,
    SensitiveContentDerivationStamp? RedactedUnder,
    bool AwaitsRepair = false,
    long ExtractedOctetCount = 0,
    long ProviderDescriptionCount = 0,
    bool RunBudgetExhausted = false)
{
    /// <summary>The outcomes that say a later reading may succeed, which is what keeps a message outstanding.</summary>
    private static readonly string[] AnswerableLater =
    [
        nameof(ImageDescriptionRefusal.ProviderTimedOut),
        nameof(ImageDescriptionRefusal.ProviderUnavailable),
        nameof(AttachmentTextExtractionOutcome.TimedOut),
    ];

    /// <summary>Gets whether any attachment contributed words to cut passages from.</summary>
    public bool YieldedText => this.Attachments.Any(attachment => attachment.HasText);

    /// <summary>Gets whether this reading is the last one this message needs, which is what lets it be stamped.</summary>
    /// <remarks>
    /// A message that is not settled keeps its readings and loses only the stamp, so the next account run reaches it
    /// again and replaces them wholesale. Two things unsettle one: a provider that may answer later, and a stored copy
    /// a repair request has been recorded for — in the second case there is nothing to read until synchronization has
    /// fetched the message again, and stamping would take it out of the walk before the repair could matter.
    /// A run that ran out of octets mid-message is a third, and the strongest: it keeps no readings either, because
    /// what it holds is a fraction of a message rather than a complete answer about one.
    /// </remarks>
    public bool IsSettled => !this.AwaitsRepair && !this.RunBudgetExhausted && !this.MayBeAnsweredLater;

    /// <summary>Gets whether a reading was refused only because a provider did not answer this time.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="ImageDescriptionRefusal" /> states that its two "say it may answer later", so a message carrying one
    /// is not finished being read: it is left unstamped and the next account run reads it again, which is the whole of
    /// the retry — nothing here waits, and the run's octet budget bounds how much repeating costs.
    /// </para>
    /// <para>
    /// <see cref="AttachmentTextExtractionOutcome.TimedOut" /> joins them for the same reason, although it comes from
    /// the local parser rather than from a provider: it is what a deadline reached under load says, so the octets are
    /// not known to be unreadable and the next run would very likely parse them. That is the distinction the extractor's
    /// other refusals are on the other side of — <see cref="AttachmentTextExtractionOutcome.Malformed" /> and
    /// <see cref="AttachmentTextExtractionOutcome.Encrypted" /> are properties of the file, which repeating cannot
    /// change. Repeating is bounded by the extraction deadline and by the run's own octet budget.
    /// </para>
    /// <para>
    /// The three configuration refusals beside them — the switch itself and the two ceilings — are deliberately not
    /// here, and the difference is who ends them. A provider outage ends on its own, so re-reading converges. A
    /// deployment that has not turned image description on is in its default state, and leaving every message carrying
    /// a picture unstamped would re-read and re-parse each of them on every run of every account for as long as the
    /// switch stays off, which is the cost the stamp exists to bound. Lifting one of those three is an operator's act
    /// over mail already stored, and what answers it is a sweep rather than the arrival pass.
    /// </para>
    /// </remarks>
    private bool MayBeAnsweredLater => this.Attachments.Any(
        attachment => AnswerableLater.Contains(attachment.Outcome, StringComparer.Ordinal));
}
