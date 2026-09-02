// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Administration;

/// <summary>Where one account's supervisor is in the loop that runs it.</summary>
/// <remarks>
/// The three working values are what an operator has to tell apart before any count means anything: an account that is
/// fetching mail now, one that is ready to and is waiting for a slot behind other accounts, and one that has finished
/// and is waiting out the pause its last run chose. Reporting the last two alike would make a deployment bounded by
/// <c>MaxConcurrentAccounts</c> look idle, which is the reading a status surface exists to prevent.
/// </remarks>
public enum MailAccountRunPhase
{
    /// <summary>No run of this account has begun in this process, which is what an account reads as until its first one starts.</summary>
    NotStarted = 0,

    /// <summary>The account has finished a run and is waiting out the delay before its next one.</summary>
    WaitingForNextRun = 1,

    /// <summary>The account is ready to run and is waiting for one of the slots that bound how many accounts run at once.</summary>
    WaitingForRunSlot = 2,

    /// <summary>The account is running: it holds a slot and its folders are being synchronized.</summary>
    Running = 3,
}
