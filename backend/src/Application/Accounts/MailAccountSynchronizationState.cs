// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Accounts;

/// <summary>How one account's local copy stands, as somebody deciding whether to trust what they are reading needs it.</summary>
/// <remarks>
/// <para>
/// The three values are what a reader has to tell apart before a timestamp means anything. A mailbox nothing has ever
/// synchronized and one synchronized an hour ago both answer a query, and both look like a mailbox; a mailbox whose
/// last refresh failed looks like either of them until something says so. How <em>old</em> the copy is stays a separate
/// fact rather than a fourth value, because what counts as too old is the reader's judgement and not this deployment's.
/// </para>
/// <para>
/// It is a plain enum because a member is a name and nothing else, which is the same reason
/// <see cref="Synchronization.Administration.MailAccountRunPhase" /> is one.
/// </para>
/// </remarks>
public enum MailAccountSynchronizationState
{
    /// <summary>No run has ever durably committed progress for the account, so its local copy holds whatever has been stored without ever having been reconciled.</summary>
    NeverSynchronized = 0,

    /// <summary>Progress has been committed and the deployment's most recent finished run of the account did not fail.</summary>
    Synchronized = 1,

    /// <summary>The deployment's most recent finished run of the account did not complete, whether or not it has ever synchronized.</summary>
    /// <remarks>It takes precedence over <see cref="NeverSynchronized" />, because an account that is failing is the one worth naming and how much of it was already stored does not change that.</remarks>
    Failing = 2,
}
