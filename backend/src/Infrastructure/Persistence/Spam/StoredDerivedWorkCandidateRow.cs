// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;

namespace MailFathom.Infrastructure.Persistence.Spam;

/// <summary>The columns an admission is decided from, as a walk over stored mail returns them.</summary>
/// <param name="MailboxAccountId">The configured account the occurrence's folder belongs to.</param>
/// <param name="Alias">The configured alias of the folder holding it now.</param>
/// <param name="StoredAt">When the occurrence was first recorded locally.</param>
/// <param name="ContentAvailability">Whether the raw MIME a classification reads is stored, is coming, or never will be.</param>
/// <param name="Verdict">What classification concluded, or <see langword="null" /> when it has reached none yet.</param>
/// <remarks>
/// The stored counterpart of <see cref="DerivedWorkCandidate" />, carried by every walk that both narrows mail through
/// <see cref="DerivedWorkAdmittedEmails" /> and then reports which of the gate's answers admitted each row. The domain
/// value objects are built here rather than in the projection, because a factory inside an <c>IQueryable</c> lambda is
/// either untranslatable or silently evaluated on the client.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredDerivedWorkCandidateRow(
    string MailboxAccountId,
    string Alias,
    DateTimeOffset StoredAt,
    StoredEmailContentAvailability ContentAvailability,
    SpamVerdict? Verdict)
{
    /// <summary>Names the answer the walk's own predicate already reached about this row.</summary>
    /// <param name="terms">The terms the whole batch is decided under, read once beside the query that selected it.</param>
    /// <returns>The admission, which is always an admitting one.</returns>
    /// <remarks>
    /// The query admits the message; this says which of the gate's answers admitted it, which is the only place a
    /// release is decidable per message. A withheld one never reaches here, so the answer is always an admitting one.
    /// </remarks>
    public DerivedWorkAdmission AdmittedUnder(DerivedWorkAdmissionTerms terms) => DerivedWorkGate.Admit(
        terms,
        new DerivedWorkCandidate(
            MailAccountId.Create(this.MailboxAccountId),
            MailFolderAlias.Create(this.Alias),
            this.StoredAt,
            this.ContentAvailability,
            this.Verdict));
}
