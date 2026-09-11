// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Administration;

/// <summary>Where one account's supervisor is in the loop that runs it.</summary>
/// <remarks>
/// <para>
/// The three working values are what an operator has to tell apart before any count means anything: an account that is
/// fetching mail now, one that is ready to and is waiting for a slot behind other accounts, and one that has finished
/// and is waiting out the pause its last run chose. Reporting the last two alike would make a deployment bounded by
/// <c>MaxConcurrentAccounts</c> look idle, which is the reading a status surface exists to prevent.
/// </para>
/// <para>
/// Three of the five describe a loop in the process that answered, and the other two describe an account no loop here
/// is running. They are apart because the remedies are opposite: an account supervised on another replica is working
/// and is read about there, and one no replica holds is the deployment having nobody fetching that mailbox.
/// </para>
/// </remarks>
public enum MailAccountRunPhase
{
    /// <summary>No run of this account has begun in this process, which is what an account this replica holds reads as until its first one starts, and what an account no replica currently holds reads as too.</summary>
    NotStarted = 0,

    /// <summary>The account has finished a run and is waiting out the delay before its next one.</summary>
    WaitingForNextRun = 1,

    /// <summary>The account is ready to run and is waiting for one of the slots that bound how many accounts run at once.</summary>
    WaitingForRunSlot = 2,

    /// <summary>The account is running: it holds a slot and its folders are being synchronized.</summary>
    Running = 3,

    /// <summary>Another replica of this deployment holds the account, so no loop here runs it and the supervision beside this phase names which one does.</summary>
    SupervisedElsewhere = 4,
}
