// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Answering.Audit;

/// <summary>One email an answering run read, and whether the answer went on to name it.</summary>
/// <param name="StoredEmailId">The stable local identity, which is the same one every other read names an email by.</param>
/// <param name="Position">Where in the run's retrieval this email was first reached, counted from zero.</param>
/// <param name="WasCited">Whether the published answer named this email as one of its sources.</param>
/// <remarks>
/// <para>
/// One shape rather than two lists, so the cited set cannot name a message the run never retrieved: the subset relation
/// is structural instead of an invariant somebody has to keep. What separates the two is that retrieval is what the run
/// <em>read</em> and a citation is what the response <em>published</em> — a response bounded to fewer citations than the
/// run retrieved emails is exactly the case where the difference is the answer.
/// </para>
/// <para>
/// The identity is the whole of what is kept. No extract, no subject, and no address: a record that stored the retrieved
/// passages would be a second copy of the mailbox with its own retention, access, and erasure obligations, for the sake
/// of a debugging convenience. What the identity buys instead is that the message can be fetched and read whole, by
/// somebody entitled to it, through the reads that already serve it.
/// </para>
/// <para>
/// The position is kept because the order a run reached mail in is part of what happened, and it survives the entry
/// losing an email to that email's own deletion — a gap in the positions then says that something was read and is gone,
/// rather than leaving a shorter list that reads as a shorter run.
/// </para>
/// </remarks>
public sealed record MailAnsweringAuditedEmail(StoredEmailId StoredEmailId, int Position, bool WasCited);
