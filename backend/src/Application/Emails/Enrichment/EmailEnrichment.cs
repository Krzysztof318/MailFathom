// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>What this deployment derived about one message, once, and wrote down.</summary>
/// <remarks>
/// <para>
/// Derived on the arrival pipeline and stored, so a list page carries it without a model call while somebody scrolls.
/// That is the whole reason it is a record rather than a call: a page of fifty rows would otherwise be fifty
/// derivations per scroll, at a cost nobody would run.
/// </para>
/// <para>
/// An empty set of marks is a real outcome and not a failure. It says a derivation ran and found nothing worth putting
/// on the row — a delivery receipt, a mailing list footer — and storing it is what takes the message out of the queue
/// so the same nothing is not paid for again. A message no derivation has reached carries no record at all, which is
/// the state a client draws as *not derived yet* rather than as *nothing to say*.
/// </para>
/// </remarks>
/// <param name="StoredEmailId">The message the derivation is about.</param>
/// <param name="Marks">What was derived, at most one mark per aspect, and empty where the derivation found nothing to say.</param>
/// <param name="DerivedAt">When the derivation ran.</param>
public sealed record EmailEnrichment(
    StoredEmailId StoredEmailId,
    IReadOnlyList<EmailEnrichmentMark> Marks,
    DateTimeOffset DerivedAt)
{
    /// <summary>Gets the mark of one aspect, or <see langword="null" /> where the derivation produced none.</summary>
    /// <param name="aspect">The reading to read.</param>
    /// <returns>The mark, or <see langword="null" />.</returns>
    public EmailEnrichmentMark? MarkOf(EmailEnrichmentAspect aspect) =>
        this.Marks.FirstOrDefault(mark => mark.Aspect == aspect);
}
