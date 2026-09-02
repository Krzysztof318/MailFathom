// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.History;

/// <summary>What classification concluded about one message, as an operator reads it back afterwards.</summary>
/// <param name="EmailId">The stored occurrence the verdict is about.</param>
/// <param name="FolderAlias">MailFathom's own name for the folder the occurrence is in.</param>
/// <param name="Verdict">What was concluded.</param>
/// <param name="DecidedBy">Which stage reached the verdict.</param>
/// <param name="Assessment">The score and the threshold it was judged against, or <see langword="null" /> when no stage produced a number.</param>
/// <param name="CorpusRevision">The scanner rule corpus the deciding stage ran under, or <see langword="null" /> when it has none.</param>
/// <param name="Profile">The settings the verdict was reached under, or the unspecified value for a record that names none.</param>
/// <param name="SignalNames">The names of the facts the verdict rests on, in the order the stages produced them.</param>
/// <param name="EvaluatedAt">When the classification was evaluated.</param>
/// <param name="RequestedMutations">The changes the verdict asked the mailbox for, which is empty where it asked for none.</param>
/// <remarks>
/// <para>
/// It is a reading of the classification record rather than a second copy of one. There is no per-run history table
/// behind this: a classification is what is believed about a message now, so the run that reached it is recoverable from
/// the instant and the profile the record already names, and inventing a row per run per message would duplicate the
/// verdict in order to record it twice.
/// </para>
/// <para>
/// The signals appear by name and never by value. A name is a method from RFC 8601, a header field, a folder alias, or a
/// scanner rule; the observation beside it is text a mail server wrote and can carry a sending domain, which is exactly
/// the second copy of the mailbox a record read back over an administrative endpoint must not become.
/// </para>
/// <para>
/// A requested change is named and pointed at rather than described. What became of it — attempted, converged, failed,
/// given up on — is the mutation trail's own record with its own retention, and restating any of it here would leave two
/// answers to one question.
/// </para>
/// </remarks>
public sealed record SpamClassificationHistoryEntry(
    StoredEmailId EmailId,
    MailFolderAlias FolderAlias,
    SpamVerdict Verdict,
    SpamClassificationStage DecidedBy,
    SpamAssessment? Assessment,
    string? CorpusRevision,
    SpamClassificationProfile Profile,
    IReadOnlyList<string> SignalNames,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<SpamClassificationRequestedMutation> RequestedMutations);

/// <summary>One change a spam verdict asked the mailbox for, named and pointed at.</summary>
/// <param name="RecordId">The durable mutation record the account's convergence pass carries.</param>
/// <param name="Mutation">What was asked for.</param>
public sealed record SpamClassificationRequestedMutation(
    MailboxMutationRecordId RecordId,
    MailboxMutation Mutation);
