// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>What one pass over an account's unread attachments did, in counts alone.</summary>
/// <remarks>
/// Nothing derived from a message appears here — no file name, no media type, no words. A count, a refusal count, and
/// two flags are what a run reports, which is the same discipline every other pass in the arrival pipeline keeps.
/// </remarks>
/// <param name="ReadEmailCount">How many messages had their attachments read and committed.</param>
/// <param name="RefusedOfferCount">How many of those the embedding backlog had no room for.</param>
/// <param name="RunBudgetExhausted">Whether the pass stopped because the account run had no octets left to read.</param>
/// <param name="EmailsRemain">Whether mail awaiting a reading was left for the next run.</param>
public sealed record MailAttachmentTextPassReport(
    int ReadEmailCount,
    int RefusedOfferCount,
    bool RunBudgetExhausted,
    bool EmailsRemain)
{
    /// <summary>Gets whether the pass found nothing to do, which is what a quiet account reports every interval.</summary>
    public bool IsEmpty => this.ReadEmailCount == 0 && !this.RunBudgetExhausted && !this.EmailsRemain;
}
