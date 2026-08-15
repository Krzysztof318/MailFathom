// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Mailboxes;

/// <summary>What a deployment reports about the mail synchronization it is running.</summary>
/// <remarks>
/// Every part below the switch is optional on the wire, and none of the absences is a fault: a deployment that has only
/// just started has run nothing, a folder no run has reached has no progress, and an account configured while the
/// process was already running has no phase of its own yet. The command reads each absence as the answer it is rather
/// than as a malformed response.
/// </remarks>
/// <param name="SynchronizationEnabled">Whether the deployment refreshes its local copy at all.</param>
/// <param name="Accounts">One entry per configured account.</param>
internal sealed record MailboxSynchronizationStatus(
    [property: JsonPropertyName("synchronizationEnabled")] bool SynchronizationEnabled,
    [property: JsonPropertyName("accounts")] IReadOnlyList<MailboxAccountSynchronization>? Accounts);

/// <summary>Where one account's synchronization stands, as the deployment reports it.</summary>
/// <param name="Account">The account, as the deployment's configuration names it.</param>
/// <param name="Phase">What the account's supervisor is doing.</param>
/// <param name="NextRunDueAt">When its next run is due, or <see langword="null" /> while it is not waiting for one.</param>
/// <param name="ConsecutiveFailureCount">How many of its runs failed in a row.</param>
/// <param name="LastRun">How its most recent finished run ended, or <see langword="null" /> when the deployment has finished none.</param>
/// <param name="Folders">One entry per folder the account maps.</param>
internal sealed record MailboxAccountSynchronization(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("phase")] string? Phase,
    [property: JsonPropertyName("nextRunDueAt")] DateTimeOffset? NextRunDueAt,
    [property: JsonPropertyName("consecutiveFailureCount")] int ConsecutiveFailureCount,
    [property: JsonPropertyName("lastRun")] MailboxAccountRun? LastRun,
    [property: JsonPropertyName("folders")] IReadOnlyList<MailboxFolderSynchronization>? Folders)
{
    /// <summary>Describes what the account's supervisor is doing, and when it next acts.</summary>
    /// <returns>The phase in the words an operator reads it by, with the instant that ends a wait.</returns>
    /// <remarks>
    /// The instant is absolute rather than a countdown, because it is the deployment's clock that decides when the run
    /// happens rather than this terminal's. A phase the deployment names and this build does not is repeated verbatim:
    /// it is a newer deployment's word for something, and inventing a reading for it would be worse than showing it.
    /// </remarks>
    internal string DescribePhase() => this.Phase switch
    {
        "Running" => "running now",
        "WaitingForRunSlot" => "ready to run, waiting for a slot behind other accounts",
        "WaitingForNextRun" => this.NextRunDueAt is { } dueAt
            ? $"waiting; next run due at {dueAt:u}"
            : "waiting for its next run",
        "NotStarted" => "not started — this deployment has not run the account since it started",
        _ => this.Phase ?? "not reported",
    };

    /// <summary>Describes the backoff the account is under, where it is under one.</summary>
    /// <returns>The consecutive failure count and what it means, or a line saying the account is on its ordinary interval.</returns>
    internal string DescribeBackoff() => this.ConsecutiveFailureCount == 0
        ? "none; the account runs on its configured interval"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{this.ConsecutiveFailureCount:N0} runs failed in a row, which is what the wait above was grown from");
}

/// <summary>What one finished run of an account produced.</summary>
/// <param name="EndedAt">When it finished.</param>
/// <param name="Failed">Whether it failed, which is what put the account into backoff.</param>
/// <param name="ScheduledFolderCount">How many folders it scheduled.</param>
/// <param name="FailedFolderCount">How many of them did not complete.</param>
/// <param name="MutationConvergenceFailed">Whether carrying the account's outstanding mailbox changes failed.</param>
internal sealed record MailboxAccountRun(
    [property: JsonPropertyName("endedAt")] DateTimeOffset EndedAt,
    [property: JsonPropertyName("failed")] bool Failed,
    [property: JsonPropertyName("scheduledFolderCount")] int ScheduledFolderCount,
    [property: JsonPropertyName("failedFolderCount")] int FailedFolderCount,
    [property: JsonPropertyName("mutationConvergenceFailed")] bool MutationConvergenceFailed)
{
    /// <summary>Describes the run in one line an operator reads.</summary>
    /// <returns>How it ended, the folder counts, and the convergence failure where there was one.</returns>
    internal string Describe()
    {
        var folders = string.Create(
            CultureInfo.InvariantCulture,
            $"{this.FailedFolderCount:N0} of {this.ScheduledFolderCount:N0} folders failed");
        var convergence = this.MutationConvergenceFailed
            ? "; carrying its outstanding mailbox changes failed too"
            : string.Empty;

        return $"{(this.Failed ? "failed" : "succeeded")} at {this.EndedAt:u}; {folders}{convergence}";
    }
}

/// <summary>Where one folder stands, as the deployment reports it.</summary>
/// <param name="Alias">MailFathom's own name for the folder.</param>
/// <param name="Mirrored">Whether the deployment mirrors the folder at all.</param>
/// <param name="UidValidity">The UID space its durable progress was made in.</param>
/// <param name="LastSeenUid">The newest UID durably processed.</param>
/// <param name="ProgressAdvancedAt">When its durable progress last moved.</param>
/// <param name="LastRun">How its most recent turn through a run ended.</param>
internal sealed record MailboxFolderSynchronization(
    [property: JsonPropertyName("alias")] string? Alias,
    [property: JsonPropertyName("mirrored")] bool Mirrored,
    [property: JsonPropertyName("uidValidity")] uint? UidValidity,
    [property: JsonPropertyName("lastSeenUid")] uint? LastSeenUid,
    [property: JsonPropertyName("progressAdvancedAt")] DateTimeOffset? ProgressAdvancedAt,
    [property: JsonPropertyName("lastRun")] MailboxFolderRun? LastRun)
{
    /// <summary>Describes how far the folder has come and when it last moved.</summary>
    /// <returns>The UID the folder is durably through and the instant it reached it, or a line saying nothing was ever committed.</returns>
    /// <remarks>
    /// This is the line that separates a folder with nothing left to fetch from one repeating a batch it cannot get
    /// past: the instant stops moving in both cases, and only the outcome beside it says which.
    /// </remarks>
    internal string DescribeProgress()
    {
        if (this.ProgressAdvancedAt is not { } advancedAt)
        {
            return "nothing committed yet — no run has stored anything for this folder";
        }

        var uid = this.LastSeenUid is { } lastSeenUid
            ? string.Create(CultureInfo.InvariantCulture, $"UID {lastSeenUid:N0}")
            : "no UID";
        var space = this.UidValidity is { } uidValidity
            ? string.Create(CultureInfo.InvariantCulture, $"UIDVALIDITY {uidValidity:N0}")
            : "an unreported UID space";

        return $"{uid} in {space}, last moved at {advancedAt:u}";
    }
}

/// <summary>What one folder's most recent turn through a run did.</summary>
/// <param name="Outcome">How the turn ended.</param>
/// <param name="EndedAt">When it ended.</param>
/// <param name="StoredEmailCount">How many occurrences it stored with their content.</param>
/// <param name="SkippedOversizedEmailCount">How many it stored as metadata only.</param>
/// <param name="UnreadableMimeEmailCount">How many stored occurrences carried MIME that could not be read.</param>
/// <param name="HasMoreEmails">Whether the folder still held unprocessed mail when the turn ended.</param>
internal sealed record MailboxFolderRun(
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("endedAt")] DateTimeOffset EndedAt,
    [property: JsonPropertyName("storedEmailCount")] int StoredEmailCount,
    [property: JsonPropertyName("skippedOversizedEmailCount")] int SkippedOversizedEmailCount,
    [property: JsonPropertyName("unreadableMimeEmailCount")] int UnreadableMimeEmailCount,
    [property: JsonPropertyName("hasMoreEmails")] bool HasMoreEmails)
{
    /// <summary>Describes the turn in one line an operator reads.</summary>
    /// <returns>What ended it, when, and what it stored where it stored anything.</returns>
    internal string Describe()
    {
        var outcome = this.DescribeOutcome();

        // Only a synchronized turn leads with its outcome, because only that one ends in counts. Every other sentence
        // ends in what the operator does next or where they read on, and a timestamp appended after that would attach
        // itself to the instruction rather than to the turn.
        return this.Outcome is "Synchronized"
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{outcome} at {this.EndedAt:u}; stored {this.StoredEmailCount:N0}, {this.SkippedOversizedEmailCount:N0} oversized, {this.UnreadableMimeEmailCount:N0} unreadable, more to fetch: {this.HasMoreEmails}")
            : $"at {this.EndedAt:u}, {outcome}";
    }

    /// <summary>States what ended the turn, and what an operator does about it where there is something to do.</summary>
    /// <remarks>
    /// The two alias outcomes name their remedy because both are corrected by editing a folder mapping rather than by
    /// waiting, which is the distinction an operator watching a folder that never advances most needs. An outcome this
    /// build does not know is repeated verbatim, because it comes from a newer deployment.
    /// </remarks>
    private string DescribeOutcome() => this.Outcome switch
    {
        "Synchronized" => "synchronized",
        "AliasUnresolved" => "the mail server advertised no folder matching this alias, so nothing was synchronized; correct the alias or configure its remote path",
        "AliasAmbiguous" => "several advertised folders matched this alias, so nothing was synchronized; configure its remote path to say which one it means",
        "DeferredAfterConcurrencyConflict" => "deferred after a concurrency conflict; the next run resumes from the stored progress",
        "DeferredAfterMailServerUnavailable" => "deferred because the mail server did not answer within its resilience budget",
        "UnexpectedFailure" => "failed unexpectedly; the deployment's log holds what happened",
        "InterruptedByShutdown" => "interrupted because the deployment was shutting down",
        _ => this.Outcome ?? "not reported",
    };
}
