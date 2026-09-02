// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Transmission;

/// <summary>States what a submission server answered about one address it was offered the message for.</summary>
/// <remarks>
/// <para>
/// The three members are the three actions available to whatever records the answer, and nothing finer. An accepted
/// address is one the transmission will carry, a temporarily refused address is one the next attempt offers again, and
/// a permanently refused address is one nothing offers again because the answer will not change.
/// </para>
/// <para>
/// Which of the two refusals a reply is comes from the reply code and the enhanced status code beside it, decided in
/// the adapter that read them. What crosses this boundary is the decision rather than the digits it was made from, so
/// no caller above re-derives it and reaches a different answer.
/// </para>
/// </remarks>
public enum MailRecipientAcceptance
{
    /// <summary>The server took the address, so the transmission that follows carries the message to it.</summary>
    /// <remarks>
    /// It says the envelope was accepted and not that the message arrived. A session that fails after this answer
    /// delivered nothing, which is why the durable record settles a recipient on the acknowledged transmission rather
    /// than on this.
    /// </remarks>
    Accepted = 0,

    /// <summary>The server refused the address for now and invited the client to return.</summary>
    RefusedTemporarily = 1,

    /// <summary>The server refused the address for good, so every later attempt receives the same answer.</summary>
    RefusedPermanently = 2,
}
