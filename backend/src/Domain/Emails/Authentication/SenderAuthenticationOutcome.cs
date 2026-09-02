// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What the receiving mail server established about who sent a message.</summary>
/// <remarks>
/// The three values answer one question and are never collapsed into a boolean. A message nobody authenticated and a
/// message whose authentication was attempted and did not hold are different facts about the sender, and both differ
/// again from a message this deployment simply cannot say anything about.
/// </remarks>
public enum SenderAuthenticationOutcome
{
    /// <summary>Nothing was established, which is the answer wherever no trusted statement was available to read.</summary>
    /// <remarks>
    /// It is a verdict rather than a missing one, and it is what a deployment whose server publishes no results sees on
    /// every message. A trusted header that never arrived, an account with no trusted authority configured, a header
    /// naming no method this reads, and a header nothing could parse all reach it: none of them is a statement about
    /// the sender, so none of them may read as one.
    /// </remarks>
    NotEstablished = 0,

    /// <summary>The receiving server attempted an identity and it did not hold.</summary>
    /// <remarks>
    /// Distinct from <see cref="NotEstablished" /> because something was actually checked. A message whose DKIM
    /// signature did not verify, or whose envelope sender failed its SPF policy, carries a statement that the claimed
    /// sender is not the sender — which is strictly more than knowing nothing.
    /// </remarks>
    Failed = 1,

    /// <summary>The receiving server verified an identity, which is the domain the verdict names.</summary>
    Authenticated = 2,
}
