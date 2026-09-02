// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.History;
using MailFathom.Application.Spam.Runs;

namespace MailFathom.Host.Api;

/// <summary>What a caller asks a whole-mailbox classification run for.</summary>
/// <param name="Account">The account to classify, as this deployment's configuration names it.</param>
/// <param name="Folders">The folder aliases to walk, or <see langword="null" /> for the scope classification is configured over.</param>
/// <param name="Apply">Whether the run may change the mailbox, which defaults to a dry run when the caller says nothing.</param>
/// <param name="Rescore">Whether mail already decided under the run's profile is scored again rather than skipped.</param>
/// <remarks>
/// Every optional answer defaults to the cautious one. The dry run above all: with filing switched on, a run over an
/// inbox is the largest single thing this feature can do to somebody's mail, so acting is something a caller says rather
/// than something they fail to switch off.
/// </remarks>
internal sealed record SpamClassificationRunRequestBody(
    string? Account,
    IReadOnlyList<string>? Folders,
    bool? Apply,
    bool? Rescore);

/// <summary>What asking for a run did, as the administrative endpoint serves it.</summary>
/// <param name="Started">Whether this request is what put the run in front of the account.</param>
/// <param name="Run">The run the account now has outstanding, which is the one already under way when nothing started.</param>
/// <remarks>
/// The two are reported together because a caller that asked twice needs both: the run is the answer either way, and
/// whether this request started it is what tells "I have just begun a walk" from "one was already going" — which also
/// tells the caller that the terms they sent are not the terms the outstanding run is walking under.
/// </remarks>
internal sealed record SpamClassificationRunStartResponse(bool Started, SpamClassificationRunResponse Run);

/// <summary>Where an account's classification run stands, as the administrative endpoint serves it.</summary>
/// <param name="Account">The account the answer is about.</param>
/// <param name="Run">The run, or <see langword="null" /> where the account has never been asked for one.</param>
internal sealed record SpamClassificationRunStateResponse(string Account, SpamClassificationRunResponse? Run);

/// <summary>One whole-mailbox classification run, as the administrative endpoint serves it.</summary>
/// <param name="RequestedAt">When the run was asked for.</param>
/// <param name="Folders">The folders the run walks.</param>
/// <param name="Posture">Whether the run writes down what its verdicts ask of the mailbox, or only works it out.</param>
/// <param name="Rescores">Whether the run scores mail already decided under its profile again.</param>
/// <param name="Profile">The settings the run is bound to, absent until the first pass picks it up.</param>
/// <param name="ClassifiedEmailCount">How many occurrences the run recorded a verdict for.</param>
/// <param name="SpamEmailCount">How many of the occurrences it reached carry a spam verdict.</param>
/// <param name="UndeterminedEmailCount">How many of them carry a verdict that concluded nothing either way.</param>
/// <param name="SkippedEmailCount">How many it passed over as already decided under its profile.</param>
/// <param name="UnclassifiableEmailCount">How many it could reach no verdict about.</param>
/// <param name="ActedEmailCount">How many it asked the mailbox to change, or would have asked for in a dry run.</param>
/// <param name="EndedAt">When the run stopped being outstanding, absent while it still is.</param>
/// <param name="Ending">How it ended, absent for exactly as long as <paramref name="EndedAt" /> is.</param>
/// <remarks>
/// Counts, instants, and MailFathom's own names, and no message is named. What a caller watching a dry run is deciding
/// on is <paramref name="ActedEmailCount" />: it is the mail the switches reach, stated before any of it reaches a mail
/// server.
/// </remarks>
internal sealed record SpamClassificationRunResponse(
    DateTimeOffset RequestedAt,
    IReadOnlyList<string> Folders,
    string Posture,
    bool Rescores,
    string? Profile,
    int ClassifiedEmailCount,
    int SpamEmailCount,
    int UndeterminedEmailCount,
    int SkippedEmailCount,
    int UnclassifiableEmailCount,
    int ActedEmailCount,
    DateTimeOffset? EndedAt,
    string? Ending)
{
    /// <summary>Describes one run for the wire.</summary>
    /// <param name="run">The run as it stands.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
    internal static SpamClassificationRunResponse For(SpamClassificationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new SpamClassificationRunResponse(
            run.RequestedAt,
            [.. run.Terms.FolderAliases.Select(static alias => alias.Value)],
            run.Terms.Posture.ToString(),
            run.Terms.Rescores,
            run.Profile.IsSpecified ? run.Profile.Value : null,
            run.ClassifiedEmailCount,
            run.SpamEmailCount,
            run.UndeterminedEmailCount,
            run.SkippedEmailCount,
            run.UnclassifiableEmailCount,
            run.ActedEmailCount,
            run.EndedAt,
            run.Ending?.ToString());
    }
}

/// <summary>One page of what classification concluded about an account's mail, as the endpoint serves it.</summary>
/// <param name="Classifications">The classifications, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record SpamClassificationPageResponse(
    IReadOnlyList<SpamClassificationResponse> Classifications,
    string? NextCursor)
{
    /// <summary>Describes one page for the wire.</summary>
    /// <param name="page">The page read back.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static SpamClassificationPageResponse For(SpamClassificationHistoryPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new SpamClassificationPageResponse(
            [.. page.Entries.Select(SpamClassificationResponse.For)],
            page.NextCursor?.Encode());
    }
}

/// <summary>What classification concluded about one message, as the administrative endpoint serves it.</summary>
/// <param name="Email">The stable local identity of the occurrence, which is the same one every other read names it by.</param>
/// <param name="Folder">MailFathom's own name for the folder the occurrence is in.</param>
/// <param name="Verdict">What was concluded.</param>
/// <param name="DecidedBy">Which stage reached the verdict.</param>
/// <param name="Score">The score reached, absent when no stage produced a number.</param>
/// <param name="Threshold">The score it was judged against, absent exactly when <paramref name="Score" /> is.</param>
/// <param name="CorpusRevision">The scanner rule corpus the deciding stage ran under, absent when it has none.</param>
/// <param name="Profile">The settings the verdict was reached under, absent on a record written before it named one.</param>
/// <param name="Signals">The names of the facts the verdict rests on, in the order the stages produced them.</param>
/// <param name="EvaluatedAt">When the classification was evaluated.</param>
/// <param name="RequestedMutations">The changes the verdict asked the mailbox for, empty where it asked for none.</param>
/// <remarks>
/// A signal appears by name and never by value, because the observation beside it is text a mail server wrote and can
/// carry a sending domain. A requested change is named and pointed at rather than described: what became of it is the
/// mutation trail's own answer, read through the audit route beside this one.
/// </remarks>
internal sealed record SpamClassificationResponse(
    Guid Email,
    string Folder,
    string Verdict,
    string DecidedBy,
    double? Score,
    double? Threshold,
    string? CorpusRevision,
    string? Profile,
    IReadOnlyList<string> Signals,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<SpamRequestedMutationResponse> RequestedMutations)
{
    /// <summary>Describes one classification for the wire.</summary>
    /// <param name="entry">The classification as it was read back.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    internal static SpamClassificationResponse For(SpamClassificationHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new SpamClassificationResponse(
            entry.EmailId.Value,
            entry.FolderAlias.Value,
            entry.Verdict.ToString(),
            entry.DecidedBy.ToString(),
            entry.Assessment?.Score,
            entry.Assessment?.Threshold,
            entry.CorpusRevision,
            entry.Profile.IsSpecified ? entry.Profile.Value : null,
            entry.SignalNames,
            entry.EvaluatedAt,
            [
                .. entry.RequestedMutations.Select(static requested => new SpamRequestedMutationResponse(
                    requested.RecordId.Value,
                    requested.Mutation.Name)),
            ]);
    }
}

/// <summary>One change a verdict asked the mailbox for, as the administrative endpoint serves it.</summary>
/// <param name="Record">The durable mutation record the account's convergence pass carries.</param>
/// <param name="Mutation">What was asked for.</param>
internal sealed record SpamRequestedMutationResponse(Guid Record, string Mutation);
