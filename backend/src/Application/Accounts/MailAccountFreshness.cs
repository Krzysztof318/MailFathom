// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Accounts;

/// <summary>How current one account's local copy is, and how current each of the folders that answer for it is.</summary>
/// <param name="Account">What configuration declares about the account.</param>
/// <param name="State">Whether the deployment's last attempt at the account succeeded, failed, found no server, or has never happened.</param>
/// <param name="LastSynchronizedAt">When any of the account's folders last durably committed progress, or <see langword="null" /> when none ever has.</param>
/// <param name="IsBehind">Whether any of the account's folders ended its last attempt with mail it had not yet taken in.</param>
/// <param name="Folders">One entry per folder local state knows of, in the order the directory read them, empty when synchronization has reached none.</param>
/// <remarks>
/// <para>
/// The first three are separable on purpose. The timestamp says how old what a reader is looking at is, the state says
/// whether it is still being refreshed, and being behind says whether there is known to be more to come — an account
/// failing since yesterday, an account nobody has written to since yesterday, and an account still catching up all
/// carry the same timestamp and are three different situations.
/// </para>
/// <para>
/// The timestamp is the newest of the account's folders rather than the oldest, because it answers "when did this
/// mailbox last take anything in": a folder that has been empty since it was mapped would otherwise hold the whole
/// account at the beginning of time. It is bounded by neither the folder count nor the message count, since it is one
/// instant however many of either there are.
/// </para>
/// <para>
/// The folders are carried because the account's own reading is derived from them and a caller drawing a folder tree
/// needs the same derivation folder by folder. A caller that only lists mailboxes reads the first four values and
/// ignores this one; what it must not do is reduce the folders a second time, because two reductions of one question
/// are two answers waiting to disagree.
/// </para>
/// </remarks>
public sealed record MailAccountFreshness(
    ServedMailAccount Account,
    MailSynchronizationState State,
    DateTimeOffset? LastSynchronizedAt,
    bool IsBehind,
    IReadOnlyList<MailFolderFreshness> Folders);
