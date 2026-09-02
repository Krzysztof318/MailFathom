// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Administration;

/// <summary>Where one account's synchronization stands in the process that is running it.</summary>
/// <remarks>
/// <para>
/// The failure count is reported beside the phase because the two together are the whole of the backoff story: a delay
/// that keeps growing while the count climbs is an account approaching a server that is refusing it, and a delay equal
/// to the configured interval with a count of zero is an account that is simply between runs. Neither reading is
/// available from either half alone.
/// </para>
/// <para>
/// It describes the running process rather than a durable record, and <see cref="MailSynchronizationRunLedger" /> holds
/// why that is the right scope: a restart resets the backoff it describes, so a count carried across one would name a
/// delay nothing is applying.
/// </para>
/// </remarks>
/// <param name="Phase">What the account's supervisor is doing.</param>
/// <param name="NextRunDueAt">When the next run is due, or <see langword="null" /> while the account is not waiting for one.</param>
/// <param name="ConsecutiveFailureCount">How many of the account's runs failed in a row; zero once one succeeds.</param>
/// <param name="LastRun">How the account's most recent finished run ended, or <see langword="null" /> when none has finished in this process.</param>
public sealed record MailAccountRunState(
    MailAccountRunPhase Phase,
    DateTimeOffset? NextRunDueAt,
    int ConsecutiveFailureCount,
    MailAccountRunReport? LastRun)
{
    /// <summary>Gets the state of an account whose supervisor has not run it yet.</summary>
    public static MailAccountRunState NotStarted { get; } = new(
        MailAccountRunPhase.NotStarted,
        NextRunDueAt: null,
        ConsecutiveFailureCount: 0,
        LastRun: null);
}

/// <summary>What one finished run of an account produced, in counts alone.</summary>
/// <remarks>
/// The folder counts are what a failed run is read by: an account whose every folder failed is a mail server that is
/// refusing, and one failing folder out of six is a mapping or a mailbox to look at. Convergence is reported apart from
/// them because it fails a run without any folder having failed, and an operator reading only the folder counts would
/// see a failed run nothing accounts for.
/// </remarks>
/// <param name="EndedAt">When the run finished.</param>
/// <param name="ScheduledFolderCount">How many folders the run scheduled.</param>
/// <param name="FailedFolderCount">How many of them did not complete.</param>
/// <param name="MutationConvergenceFailed">Whether carrying the account's outstanding mailbox changes failed, which fails the run on its own.</param>
public sealed record MailAccountRunReport(
    DateTimeOffset EndedAt,
    int ScheduledFolderCount,
    int FailedFolderCount,
    bool MutationConvergenceFailed)
{
    /// <summary>Gets whether the run failed, which is what puts the account into backoff.</summary>
    public bool Failed => this.FailedFolderCount > 0 || this.MutationConvergenceFailed;
}
