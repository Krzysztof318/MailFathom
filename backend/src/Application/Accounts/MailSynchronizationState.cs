// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Accounts;

/// <summary>How one local copy stands, as somebody deciding whether to trust what they are reading needs it.</summary>
/// <remarks>
/// <para>
/// One vocabulary for a folder and for the account holding it, because the question is the same at both sizes and a
/// reader moving between a mailbox list and a folder tree must not have to learn it twice. An account's value is the
/// reduction of its folders' — <see cref="MailAccountFreshnessReader" /> is where that reduction is taken, once.
/// </para>
/// <para>
/// The values are what a reader has to tell apart before a timestamp means anything. A mailbox nothing has ever
/// synchronized and one synchronized an hour ago both answer a query, and both look like a mailbox; a mailbox whose
/// last refresh failed looks like either of them until something says so, and one whose mail server is not answering
/// looks like a failure until something separates the two. How <em>old</em> the copy is stays a separate fact rather
/// than a value here, because what counts as too old is the reader's judgement and not this deployment's; so does
/// whether the copy is behind, which a failing refresh and a working one can both leave it.
/// </para>
/// <para>
/// It is a plain enum because a member is a name and nothing else, which is the same reason
/// <see cref="Synchronization.Administration.MailAccountRunPhase" /> is one.
/// </para>
/// </remarks>
public enum MailSynchronizationState
{
    /// <summary>No run has ever durably committed progress, so the local copy holds whatever has been stored without ever having been reconciled.</summary>
    NeverSynchronized = 0,

    /// <summary>Progress has been committed and the deployment's most recent finished attempt did not fail.</summary>
    Synchronized = 1,

    /// <summary>The deployment's most recent finished attempt did not complete, whether or not it has ever synchronized.</summary>
    /// <remarks>It takes precedence over <see cref="NeverSynchronized" />, because something that is failing is the fact worth naming and how much of it was already stored does not change that.</remarks>
    Failing = 2,

    /// <summary>The mail server did not serve the deployment within its resilience budget, so the copy is not being refreshed and nothing here is wrong with it.</summary>
    /// <remarks>
    /// It is separated from <see cref="Failing" /> because the two ask different things of whoever reads them: an
    /// unreachable mailbox is waited out or looked at on the server, and a failing one is a mapping, a credential, or a
    /// defect. An account reaches it only when unreachability is the whole of what went wrong — a run that also failed
    /// some other way reached the server, so it is <see cref="Failing" /> instead.
    /// </remarks>
    Unreachable = 3,
}
