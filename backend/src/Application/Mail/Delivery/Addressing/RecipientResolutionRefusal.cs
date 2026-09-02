// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Addressing;

/// <summary>States why a recipient an author named resolved to nobody, in terms that name no address.</summary>
/// <param name="Reason">What stopped it.</param>
/// <param name="MatchedContactCount">How many contacts carried the name, when the reason is an ambiguous one; otherwise <see langword="null" />.</param>
/// <remarks>
/// The pair is the whole of what a refusal may carry. A resolution knows a name, an address the author chose, and — for
/// an ambiguous name — several people's records, and every one of those is personal data of somebody who is not this
/// mailbox's owner. So the count is reported and nothing that was counted is, and an address is never echoed back: a
/// caller that supplied one already holds it, and a caller that supplied none must not learn one from a refusal.
/// </remarks>
public sealed record RecipientResolutionRefusal(
    RecipientResolutionRefusalReason Reason,
    int? MatchedContactCount = null)
{
    /// <summary>Gets the published identity of this refusal.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the reason is not one this system declares, which a refusal built from a cast integer is.</exception>
    public MailFathomErrorCode Failure => this.Reason switch
    {
        RecipientResolutionRefusalReason.ContactUnknown => MailFathomErrorCode.OutgoingEmailContactUnknown,
        RecipientResolutionRefusalReason.ContactNameAmbiguous =>
            MailFathomErrorCode.OutgoingEmailContactNameAmbiguous,
        RecipientResolutionRefusalReason.ContactAddressNotHeld =>
            MailFathomErrorCode.OutgoingEmailContactAddressNotHeld,
        _ => throw new InvalidOperationException(
            "The recipient resolution refusal reason is not one this system declares."),
    };
}
