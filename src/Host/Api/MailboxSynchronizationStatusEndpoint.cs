// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Administration;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves what this deployment's mail synchronization is doing, account by account and folder by folder.</summary>
/// <remarks>
/// <para>
/// It exists because nothing else a deployment ships answers the question. A synchronization run that is failing,
/// backing off, or standing still on one folder is visible in telemetry and in the log, and an operator running without
/// a metrics stack meets it as mail that does not arrive — which reads as an empty mailbox rather than as a stalled
/// worker.
/// </para>
/// <para>
/// It is here rather than on the MCP surface for the reason every administrative route is: what it reports is the state
/// of the service rather than anything a model reasons over, and the credential that bounds administrative access is
/// what should bound reading how a deployment's workers are faring.
/// <strong>Every authenticated caller may perform every administrative operation</strong>, which
/// <see cref="MailboxRefreshTokenEndpoint" /> states in full.
/// </para>
/// <para>
/// Nothing it answers with is mail. Configured account identifiers and folder aliases, a phase, counts, UIDs, and
/// timestamps are the whole of it — never a subject, an address, a remote folder path, or the detail of an exception.
/// </para>
/// </remarks>
internal static class MailboxSynchronizationStatusEndpoint
{
    /// <summary>The route reporting where synchronization stands, relative to the administrative prefix.</summary>
    internal const string StatusRoute = "/mailbox/synchronization";

    /// <summary>Maps the route into the administrative group, so it inherits its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapMailboxSynchronizationStatus(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(StatusRoute, ReadStatusAsync);
    }

    /// <summary>Reports where each configured account's synchronization stands.</summary>
    /// <param name="reader">Composes the answer from configuration, the running process, and the durable checkpoints.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the state, on every deployment including one that synchronizes nothing.</returns>
    /// <remarks>
    /// It never refuses. A deployment configuring no account, one that has switched synchronization off, and one whose
    /// process has only just started are supported states rather than errors, and the last of them is the reading an
    /// operator most needs to be given rather than left to infer from an empty answer.
    /// </remarks>
    internal static async Task<Ok<MailSynchronizationStatusResponse>> ReadStatusAsync(
        [FromServices] MailSynchronizationStatusReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var status = await reader.ReadAsync(cancellationToken);

        return TypedResults.Ok(MailSynchronizationStatusResponse.For(status));
    }
}

/// <summary>What the administrative endpoint reports about this deployment's synchronization.</summary>
/// <param name="SynchronizationEnabled">Whether this deployment refreshes its local copy at all.</param>
/// <param name="Accounts">One entry per configured account, ordered ordinally by identifier.</param>
internal sealed record MailSynchronizationStatusResponse(
    bool SynchronizationEnabled,
    IReadOnlyList<MailAccountSynchronizationResponse> Accounts)
{
    /// <summary>Describes one status answer on the wire.</summary>
    /// <param name="status">The composed status.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="status" /> is <see langword="null" />.</exception>
    internal static MailSynchronizationStatusResponse For(MailSynchronizationStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new MailSynchronizationStatusResponse(
            status.SynchronizationEnabled,
            [.. status.Accounts.Select(MailAccountSynchronizationResponse.For)]);
    }
}

/// <summary>Where one account's synchronization stands.</summary>
/// <param name="Account">The account, as configuration names it.</param>
/// <param name="Phase">What the account's supervisor is doing, as the phase's own name.</param>
/// <param name="NextRunDueAt">When its next run is due, or <see langword="null" /> while it is not waiting for one.</param>
/// <param name="ConsecutiveFailureCount">How many of its runs failed in a row; zero once one succeeds.</param>
/// <param name="LastRun">How its most recent finished run ended, or <see langword="null" /> when none has finished in this process.</param>
/// <param name="Folders">One entry per folder the account maps, ordered ordinally by alias.</param>
internal sealed record MailAccountSynchronizationResponse(
    string Account,
    string Phase,
    DateTimeOffset? NextRunDueAt,
    int ConsecutiveFailureCount,
    MailAccountRunResponse? LastRun,
    IReadOnlyList<MailFolderSynchronizationResponse> Folders)
{
    /// <summary>Describes one account on the wire.</summary>
    /// <param name="account">The account's status.</param>
    /// <returns>The response body.</returns>
    internal static MailAccountSynchronizationResponse For(MailAccountSynchronizationStatus account) =>
        new(
            account.AccountId.Value,
            account.Run.Phase.ToString(),
            account.Run.NextRunDueAt,
            account.Run.ConsecutiveFailureCount,
            account.Run.LastRun is { } lastRun ? MailAccountRunResponse.For(lastRun) : null,
            [.. account.Folders.Select(MailFolderSynchronizationResponse.For)]);
}

/// <summary>What one finished run of an account produced.</summary>
/// <param name="EndedAt">When it finished.</param>
/// <param name="Failed">Whether it failed, which is what put the account into backoff.</param>
/// <param name="ScheduledFolderCount">How many folders it scheduled.</param>
/// <param name="FailedFolderCount">How many of them did not complete.</param>
/// <param name="MutationConvergenceFailed">Whether carrying the account's outstanding mailbox changes failed, which fails a run on its own.</param>
internal sealed record MailAccountRunResponse(
    DateTimeOffset EndedAt,
    bool Failed,
    int ScheduledFolderCount,
    int FailedFolderCount,
    bool MutationConvergenceFailed)
{
    /// <summary>Describes one finished run on the wire.</summary>
    /// <param name="run">The run's report.</param>
    /// <returns>The response body.</returns>
    internal static MailAccountRunResponse For(MailAccountRunReport run) => new(
        run.EndedAt,
        run.Failed,
        run.ScheduledFolderCount,
        run.FailedFolderCount,
        run.MutationConvergenceFailed);
}

/// <summary>Where one folder stands.</summary>
/// <param name="Alias">MailFathom's own name for the folder.</param>
/// <param name="Mirrored">Whether this deployment mirrors the folder at all.</param>
/// <param name="UidValidity">The UID space its durable progress was made in, or <see langword="null" /> when it has none.</param>
/// <param name="LastSeenUid">The newest UID durably processed, or <see langword="null" /> when it has no progress or its space is empty.</param>
/// <param name="ProgressAdvancedAt">When its durable progress last moved, or <see langword="null" /> when synchronization has never committed any.</param>
/// <param name="LastRun">How its most recent turn through a run ended, or <see langword="null" /> when no run of this process has taken one.</param>
internal sealed record MailFolderSynchronizationResponse(
    string Alias,
    bool Mirrored,
    uint? UidValidity,
    uint? LastSeenUid,
    DateTimeOffset? ProgressAdvancedAt,
    MailFolderRunResponse? LastRun)
{
    /// <summary>Describes one folder on the wire.</summary>
    /// <param name="folder">The folder's status.</param>
    /// <returns>The response body.</returns>
    internal static MailFolderSynchronizationResponse For(MailFolderSynchronizationStatus folder) => new(
        folder.Alias.Value,
        folder.Mirrored,
        folder.UidValidity?.Value,
        folder.LastSeenUid?.Value,
        folder.ProgressAdvancedAt,
        folder.LastRun is { } lastRun ? MailFolderRunResponse.For(lastRun) : null);
}

/// <summary>What one folder's most recent turn through a run did.</summary>
/// <param name="Outcome">How the turn ended, as the outcome's own name.</param>
/// <param name="EndedAt">When it ended.</param>
/// <param name="StoredEmailCount">How many occurrences it stored with their content.</param>
/// <param name="SkippedOversizedEmailCount">How many it stored as metadata only.</param>
/// <param name="UnreadableMimeEmailCount">How many stored occurrences carried MIME that could not be read.</param>
/// <param name="HasMoreEmails">Whether the folder still held unprocessed mail when the turn ended.</param>
internal sealed record MailFolderRunResponse(
    string Outcome,
    DateTimeOffset EndedAt,
    int StoredEmailCount,
    int SkippedOversizedEmailCount,
    int UnreadableMimeEmailCount,
    bool HasMoreEmails)
{
    /// <summary>Describes one folder turn on the wire.</summary>
    /// <param name="run">The turn's report.</param>
    /// <returns>The response body.</returns>
    internal static MailFolderRunResponse For(MailFolderRunReport run) => new(
        run.Outcome.ToString(),
        run.EndedAt,
        run.StoredEmailCount,
        run.SkippedOversizedEmailCount,
        run.UnreadableMimeEmailCount,
        run.HasMoreEmails);
}
