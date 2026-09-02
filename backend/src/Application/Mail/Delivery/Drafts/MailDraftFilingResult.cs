// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>Reports what one attempt against the mailbox did for one draft, already durable by the time it is read.</summary>
/// <param name="DraftId">The draft the attempt was about.</param>
/// <param name="Outcome">What it did.</param>
/// <param name="Failure">The code identifying what ended it, or <see langword="null" /> when nothing did.</param>
/// <param name="Divergence">Why the tracked copy was left alone, or <see langword="null" /> when none was.</param>
/// <remarks>
/// It names the draft rather than the message, and carries a code rather than a sentence. Nothing here is a subject, an
/// address, or a line of what somebody wrote, which is what lets the whole result reach a log line and a counter.
/// </remarks>
public sealed record MailDraftFilingResult(
    MailDraftId DraftId,
    MailDraftFilingOutcome Outcome,
    MailFathomErrorCode? Failure,
    MailDraftDivergenceReason? Divergence);
