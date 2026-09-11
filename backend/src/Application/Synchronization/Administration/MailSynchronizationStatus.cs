// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Application.Emails.AttachmentText.Administration;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Administration;

/// <summary>What this deployment's mail synchronization is currently doing, account by account and folder by folder.</summary>
/// <remarks>
/// <para>
/// One value rather than a metrics stack, because the question it answers — is my mail being kept up to date — has
/// answers that look nothing alike from outside: synchronization switched off, an account backing off a server that is
/// refusing it, a folder whose alias names nothing, a folder still backfilling, and a folder that has been repeating one
/// batch since yesterday. Every one of them reaches an operator as mail that does not arrive, which reads as an empty
/// mailbox rather than as a stalled worker.
/// </para>
/// <para>
/// It answers about the deployment rather than about the replica the request reached. Which replica supervises an
/// account is read from the lease table every replica shares, so an operator asking twice is told the same thing twice;
/// what only the answering process knows is reported beside <see cref="Replica" /> and never as the deployment's.
/// </para>
/// <para>
/// Nothing here is mail. Configured account identifiers and folder aliases, a phase, counts, UIDs, instants, and a
/// replica identity are the whole of it — no subject, no address, no remote folder path, and no exception detail.
/// </para>
/// </remarks>
/// <param name="Replica">
/// The replica that composed this answer. It is here so the parts only one process can answer for — the phase, the
/// backoff, and the last run of an account this replica supervises — are attributable to a process an operator can
/// read a log for, rather than reading as the deployment's.
/// </param>
/// <param name="SynchronizationEnabled">Whether this deployment refreshes its local copy at all, which is the answer that makes every count below still.</param>
/// <param name="Accounts">One entry per configured account, ordered ordinally by identifier.</param>
public sealed record MailSynchronizationStatus(
    ReplicaIdentity Replica,
    bool SynchronizationEnabled,
    IReadOnlyList<MailAccountSynchronizationStatus> Accounts);

/// <summary>Which replica of a deployment is supervising one account, and how long its hold runs for.</summary>
/// <remarks>
/// <para>
/// Read from the lease table rather than from anything a process remembers, which is what makes it the deployment's
/// answer: a replica that has never supervised an account still reports the replica that is supervising it.
/// </para>
/// <para>
/// The instant is when the hold would run out if its holder stopped renewing, and not a promise about the work. A hold
/// held well into the future is a replica that was answering the database moments ago, which is the whole of what the
/// deployment durably knows about a run in another process — the scheduling state that would say more is deliberately
/// not durable, for the reason <see cref="MailSynchronizationRunLedger" /> records.
/// </para>
/// </remarks>
/// <param name="Replica">The replica holding the account's supervision.</param>
/// <param name="HeldUntil">When the hold expires unless it is renewed before then.</param>
public sealed record MailAccountSupervision(ReplicaIdentity Replica, DateTimeOffset HeldUntil);

/// <summary>Where one account's synchronization stands, and what each of its folders last did.</summary>
/// <param name="AccountId">The account, as configuration names it.</param>
/// <param name="Supervision">
/// Which replica holds the account, or <see langword="null" /> when no replica does — a deployment that synchronizes
/// nothing, one whose replicas have all stopped, or an account whose hold has expired and has not yet been taken again.
/// </param>
/// <param name="Run">
/// What the account's supervisor is doing and how its last run ended. It describes the answering replica's own loop, so
/// an account <paramref name="Supervision" /> names another replica for reports
/// <see cref="MailAccountRunPhase.SupervisedElsewhere" /> and nothing further: what that replica is doing is read on it.
/// </param>
/// <param name="Folders">One entry per folder the account maps, ordered ordinally by alias.</param>
/// <param name="AttachmentText">
/// How much of this account's attachment and image content has been read, how much waits, and what was skipped. It sits
/// beside the folder progress because it is the same question asked of the stage behind the cut: a folder can be fully
/// fetched while every contract in it is still unread, and an operator watching results that do not appear needs to see
/// which of the two is behind.
/// </param>
public sealed record MailAccountSynchronizationStatus(
    MailAccountId AccountId,
    MailAccountSupervision? Supervision,
    MailAccountRunState Run,
    IReadOnlyList<MailFolderSynchronizationStatus> Folders,
    AttachmentDerivationCoverage AttachmentText);

/// <summary>Where one folder stands: what its last turn did, and how far its durable progress has come.</summary>
/// <remarks>
/// The two halves are reported together because neither settles the question alone. A last turn that succeeded says
/// nothing about whether the folder is advancing — a run can end cleanly having stored nothing for a folder that is
/// still hours behind — and progress that has not moved says nothing about why, which is what the outcome names.
/// </remarks>
/// <param name="Alias">MailFathom's own name for the folder.</param>
/// <param name="Mirrored">Whether this deployment mirrors the folder at all; a mapped folder it does not mirror is never scheduled.</param>
/// <param name="UidValidity">The UID space the durable progress was made in, or <see langword="null" /> when the folder has none.</param>
/// <param name="LastSeenUid">The newest UID durably processed, or <see langword="null" /> when the folder has no progress or its space is empty.</param>
/// <param name="ProgressAdvancedAt">When the durable progress last moved, or <see langword="null" /> when synchronization has never committed any.</param>
/// <param name="LastRun">How the folder's most recent turn through a run ended, or <see langword="null" /> when no run of this process has taken one.</param>
public sealed record MailFolderSynchronizationStatus(
    MailFolderAlias Alias,
    bool Mirrored,
    ImapUidValidity? UidValidity,
    ImapUid? LastSeenUid,
    DateTimeOffset? ProgressAdvancedAt,
    MailFolderRunReport? LastRun);
