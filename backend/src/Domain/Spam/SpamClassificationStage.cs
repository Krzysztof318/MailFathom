// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>Names which stage of classification reached a verdict.</summary>
/// <remarks>
/// The stages are ordered by how much they cost and how much context they had. The deterministic stage reads what the
/// receiving server already concluded and needs no scanner, no network, and no model; a scanner re-reads the message
/// after delivery, without the network context the receiving server had, and therefore never overrules a fact the
/// deterministic stage established.
/// </remarks>
public enum SpamClassificationStage
{
    /// <summary>What the message itself carried: sender authentication, a provider's own spam headers, and where the mailbox filed it.</summary>
    Deterministic = 0,

    /// <summary>An external scanner's score over the whole message, reached under a named rule corpus.</summary>
    Scanner = 1,
}
