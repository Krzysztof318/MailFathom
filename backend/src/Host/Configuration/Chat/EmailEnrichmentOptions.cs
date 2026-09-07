// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Declares whether arriving mail is read into the marks a list row draws.</summary>
/// <remarks>
/// <para>
/// A block inside <c>Chat</c> rather than a root of its own, because a derivation runs against the declared chat
/// endpoint and has nowhere to send a message without one. An operator who removes the chat section has removed this
/// with it, which is the honest reading of what they did.
/// </para>
/// <para>
/// Off by default, and off is not a lesser deployment. A list then draws what every instance drew before this existed —
/// a subject, a sender, and the opening of the message — and a client renders the absence of marks as a row nothing has
/// been derived about rather than as something that failed.
/// </para>
/// <para>
/// It is one switch and no numbers, which is deliberate. What one derivation costs is a provider call over a handful of
/// a message's passages, and what a deployment may spend on those in total is already declared once, in
/// <c>MailAnswering</c>: every derivation is admitted against the same period ceilings a question is and counted by the
/// same ledgers. That has a consequence worth stating plainly — enrichment and the questions somebody asks compete for
/// one allowance, so an instance enriching a large mailbox for the first time will refuse questions it would otherwise
/// have answered until the backlog drains. Raising those ceilings is the remedy, and it is the operator's decision
/// because it is their provider bill.
/// </para>
/// </remarks>
internal sealed class EmailEnrichmentOptions
{
    /// <summary>Gets or sets whether arriving mail is read into marks as it is stored.</summary>
    public bool Enabled { get; set; }
}
