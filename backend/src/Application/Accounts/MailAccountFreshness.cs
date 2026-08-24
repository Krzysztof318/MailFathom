// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Accounts;

/// <summary>How current one account's local copy is, in the two facts that answer it without naming a folder.</summary>
/// <param name="Account">What configuration declares about the account.</param>
/// <param name="State">Whether the deployment's last attempt at the account succeeded, failed, or has never happened.</param>
/// <param name="LastSynchronizedAt">When any of the account's folders last durably committed progress, or <see langword="null" /> when none ever has.</param>
/// <remarks>
/// <para>
/// The two are separable on purpose. The timestamp says how old what a reader is looking at is, and the state says
/// whether it is still being refreshed — an account failing since yesterday and an account nobody has written to since
/// yesterday carry the same timestamp and are not the same situation.
/// </para>
/// <para>
/// The timestamp is the newest of the account's folders rather than the oldest, because it answers "when did this
/// mailbox last take anything in": a folder that has been empty since it was mapped would otherwise hold the whole
/// account at the beginning of time. It is bounded by neither the folder count nor the message count, since it is one
/// instant however many of either there are.
/// </para>
/// </remarks>
public sealed record MailAccountFreshness(
    ServedMailAccount Account,
    MailAccountSynchronizationState State,
    DateTimeOffset? LastSynchronizedAt);
