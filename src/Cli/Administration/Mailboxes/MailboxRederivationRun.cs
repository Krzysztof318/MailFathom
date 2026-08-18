// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Mailboxes;

/// <summary>What asking for a re-derivation did.</summary>
/// <param name="Started">Whether this request is what put the run in front of the scope.</param>
/// <param name="Queued">Whether the work carrying the run is waiting in the deployment's queue.</param>
/// <param name="Run">The run the scope now has, which is the one already going when nothing started.</param>
/// <remarks>
/// A request that started nothing is an answer rather than a refusal: the mail is going to be re-read, and the run
/// reported is the one doing it. A request that started one but reports nothing queued met a full queue, which is
/// backpressure rather than a failure — the run stands and asking again is what carries it.
/// </remarks>
internal sealed record MailboxRederivationStart(
    [property: JsonPropertyName("started")] bool Started,
    [property: JsonPropertyName("queued")] bool Queued,
    [property: JsonPropertyName("run")] MailboxRederivationRun? Run);

/// <summary>Where a scope's re-derivation stands.</summary>
/// <param name="Account">The account the answer is about.</param>
/// <param name="Folder">The alias the question was narrowed to, or nothing when it covers the whole account.</param>
/// <param name="Run">The run, or <see langword="null" /> where the scope has never been asked for one.</param>
internal sealed record MailboxRederivationState(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("folder")] string? Folder,
    [property: JsonPropertyName("run")] MailboxRederivationRun? Run);

/// <summary>One re-derivation of a scope's stored mail, and how far the deployment has carried it.</summary>
/// <param name="Account">The account the run walks.</param>
/// <param name="Folder">The alias it was narrowed to, or nothing when it covers the whole account.</param>
/// <param name="RequestedAt">When the run was asked for.</param>
/// <param name="IsOutstanding">Whether the run is still waiting to be carried further.</param>
/// <param name="RederivedEmailCount">How many stored emails the run has re-read and written metadata for.</param>
/// <param name="UnreadableEmailCount">How many carried MIME no reader could parse, which the run stepped over.</param>
/// <param name="MissingContentEmailCount">How many no longer had raw MIME to re-read.</param>
/// <param name="EndedAt">When the run reached the end of its scope, absent while it has not.</param>
internal sealed record MailboxRederivationRun(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("folder")] string? Folder,
    [property: JsonPropertyName("requestedAt")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("isOutstanding")] bool IsOutstanding,
    [property: JsonPropertyName("rederivedEmailCount")] int RederivedEmailCount,
    [property: JsonPropertyName("unreadableEmailCount")] int UnreadableEmailCount,
    [property: JsonPropertyName("missingContentEmailCount")] int MissingContentEmailCount,
    [property: JsonPropertyName("endedAt")] DateTimeOffset? EndedAt)
{
    /// <summary>Describes where the run stands in one line an operator reads.</summary>
    /// <returns>Whether it is still going, or when it finished.</returns>
    /// <remarks>
    /// A run in flight reports no estimate of what is left. What a pass reaches depends on how much MIME the mail it
    /// walks carries, so a figure invented here would be a prediction rather than a reading.
    /// </remarks>
    internal string DescribeState() => this.EndedAt is { } endedAt
        ? $"finished at {endedAt.ToString("u", CultureInfo.InvariantCulture)}"
        : "under way";

    /// <summary>Describes what the run has re-read so far, in counts and nothing derived from a message.</summary>
    /// <returns>The counts, grouped invariantly for the reason every other figure this tool prints is.</returns>
    internal string DescribeProgress() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.RederivedEmailCount:N0} re-read, {this.UnreadableEmailCount:N0} unparseable, {this.MissingContentEmailCount:N0} no longer stored");
}
