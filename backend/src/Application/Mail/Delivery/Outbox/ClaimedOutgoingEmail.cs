// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>One queued send a claim handed to one attempt, with the lease that attempt holds it under.</summary>
/// <remarks>
/// The attempt count on the record includes this attempt, because the claim counted it. Counting at the claim rather
/// than in whatever transmits is what makes the bound survive a crash loop: a process that dies mid-attempt never
/// reaches a line that would have counted it, and a send that kills the host every time would otherwise be attempted
/// forever.
/// </remarks>
/// <param name="Record">The send this attempt holds, as the claim left it.</param>
/// <param name="Lease">The lease this attempt holds it under.</param>
public sealed record ClaimedOutgoingEmail(OutgoingEmailRecord Record, OutgoingEmailLease Lease);
