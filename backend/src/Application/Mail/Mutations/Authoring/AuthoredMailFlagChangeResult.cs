// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>What one caller's flag and keyword change became: a durable record per value it asked for.</summary>
/// <param name="StoredEmailId">The email the change was asked for.</param>
/// <param name="AccountId">The account that email belongs to, which is the account whose run will carry the change.</param>
/// <param name="FolderAlias">The operator's own name for the folder the email is in.</param>
/// <param name="Recorded">One entry per value the request named, in the order the request states them.</param>
/// <remarks>
/// It reports what was written down rather than what a mail server has done, because at the moment this is produced no
/// command has gone out. That is the honest answer and it is also the useful one: the record is what survives a crash,
/// what convergence resumes, and what an operator reads when a change has not arrived, so its identity is what a caller
/// needs in hand.
/// </remarks>
public sealed record AuthoredMailFlagChangeResult(
    StoredEmailId StoredEmailId,
    MailAccountId AccountId,
    MailFolderAlias FolderAlias,
    IReadOnlyList<RecordedMailFlagMutation> Recorded);

/// <summary>One value a change asked for, and the durable record that now carries it.</summary>
/// <param name="Mutation">The change that was written down, under the name every log line and counter uses for it.</param>
/// <param name="RecordId">The record everything afterwards refers to that change by.</param>
/// <param name="Lifecycle">Where that record stands, which is pending for a change nothing has attempted yet.</param>
/// <remarks>
/// The lifecycle is reported rather than assumed to be pending, because a request repeated under the identity that
/// already produced one is answered with the record it produced. A caller retrying a call therefore learns that its
/// change has already been made instead of being told a second one was opened.
/// </remarks>
public sealed record RecordedMailFlagMutation(
    MailboxMutation Mutation,
    MailboxMutationRecordId RecordId,
    MailboxMutationLifecycle Lifecycle);
