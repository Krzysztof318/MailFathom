// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
/// Nothing here is mail. Configured account identifiers and folder aliases, a phase, counts, UIDs, and instants are the
/// whole of it — no subject, no address, no remote folder path, and no exception detail.
/// </para>
/// </remarks>
/// <param name="SynchronizationEnabled">Whether this deployment refreshes its local copy at all, which is the answer that makes every count below still.</param>
/// <param name="Accounts">One entry per configured account, ordered ordinally by identifier.</param>
public sealed record MailSynchronizationStatus(
    bool SynchronizationEnabled,
    IReadOnlyList<MailAccountSynchronizationStatus> Accounts);

/// <summary>Where one account's synchronization stands, and what each of its folders last did.</summary>
/// <param name="AccountId">The account, as configuration names it.</param>
/// <param name="Run">What the account's supervisor is doing and how its last run ended.</param>
/// <param name="Folders">One entry per folder the account maps, ordered ordinally by alias.</param>
public sealed record MailAccountSynchronizationStatus(
    MailAccountId AccountId,
    MailAccountRunState Run,
    IReadOnlyList<MailFolderSynchronizationStatus> Folders);

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
