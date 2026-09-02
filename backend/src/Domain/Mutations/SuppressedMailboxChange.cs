// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Mutations;

/// <summary>One change a synchronization run discovered and did not raise, because MailFathom itself had made it.</summary>
/// <param name="Kind">What the mail server reported had changed.</param>
/// <param name="Mutation">The change MailFathom had asked for, which is the same word its log line and its counter use.</param>
/// <param name="StoredEmailId">The local email the change is about.</param>
/// <param name="MutationRecordId">The durable record that answered <em>was this ours</em>.</param>
/// <remarks>
/// <para>
/// Suppression exists so that a rule which files mail files it once. Every mutation MailFathom performs is a change
/// synchronization later discovers, so without provenance a rule matching on folder would file a message, discover it
/// in its new folder, match again, and go round for as long as the mailbox is watched — at the cost of an IMAP command
/// a lap. Two rules with overlapping conditions do the same to each other.
/// </para>
/// <para>
/// It is decided from the record and from nothing else. A cycle limit or a rate limit would be the alternative and both
/// are worse: the first stops a loop only after it has run several times, and the second stops legitimate work at the
/// same threshold. The record answers the question exactly, so the answer is a fact rather than an inference.
/// </para>
/// <para>
/// This value is what makes a suppression explainable. A rule that appears not to have fired is otherwise the same
/// thing to read as a rule that never matched, so a run reports which change it withheld and which record accounted for
/// it. It names identities and a mutation name only, never a subject, an address, or any other part of the message.
/// </para>
/// </remarks>
public sealed record SuppressedMailboxChange(
    MailboxChangeKind Kind,
    MailboxMutation Mutation,
    StoredEmailId StoredEmailId,
    MailboxMutationRecordId MutationRecordId);
