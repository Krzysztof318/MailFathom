// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>Pairs one address with the header role it was written in.</summary>
/// <param name="Role">Which header carried the address.</param>
/// <param name="Address">The normalized address.</param>
public sealed record EmailParticipant(EmailAddressRole Role, EmailAddress Address)
{
    /// <summary>The greatest number of participants one header role contributes to anything read out of a message.</summary>
    /// <remarks>
    /// Nothing between a sender and this system bounds how many addresses a header may carry, and every reader of a
    /// message publishes what it found: a listing filters on it, a content read returns it. A message addressed to more
    /// mailboxes than this is a list expansion whose members no reader asks about individually, so the excess is
    /// dropped where the message is parsed rather than allowed to reach a result whose size the sender then decides.
    /// <para>
    /// The persisted columns carry a bound of their own, deliberately. This one bounds what a parse publishes and that
    /// one bounds what a column stores, and the two would answer differently if a later schema change moved one of them.
    /// </para>
    /// </remarks>
    public const int MaximumPerRole = 256;
}
