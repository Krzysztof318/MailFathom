// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Content;

/// <summary>Where the deployment's move of its stored content stands, and how much the database still holds.</summary>
/// <param name="Available">Whether the deployment has an object backend its content could be moved into at all.</param>
/// <param name="Run">The move it last had, or <see langword="null" /> when none was ever asked for.</param>
/// <param name="RemainingPayloadCount">How many payloads of any kind the database still holds.</param>
/// <param name="RemainingByteCount">How many bytes of raw MIME they carry between them.</param>
/// <remarks>
/// The backlog is reported whether or not a move exists, because it is what an operator weighs before asking for one —
/// and a deployment storing content in the database answers it too, which is what makes this command useful before the
/// switch rather than only after it.
/// </remarks>
internal sealed record ContentMoveReport(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("run")] ContentMoveRun? Run,
    [property: JsonPropertyName("remainingPayloadCount")] long RemainingPayloadCount,
    [property: JsonPropertyName("remainingByteCount")] long RemainingByteCount)
{
    /// <summary>Describes what is left, in counts and volume rather than in an estimate of time.</summary>
    /// <returns>The backlog, grouped invariantly for the reason every other figure this tool prints is.</returns>
    /// <remarks>
    /// No estimate of when a move would finish. What a pass carries depends on how much MIME the payloads it reaches
    /// hold and on what else the deployment is doing, so a figure invented here would be a prediction rather than a
    /// reading.
    /// </remarks>
    internal string DescribeBacklog() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.RemainingPayloadCount:N0} payloads carrying {this.RemainingByteCount:N0} bytes");
}

/// <summary>One move of stored content into the object backend, and how far the deployment has carried it.</summary>
/// <param name="State">What the move is doing, as the deployment's own word for it.</param>
/// <param name="RequestedAt">When the move was asked for.</param>
/// <param name="CopiedPayloadCount">How many payloads it has copied, verified, and repointed at the object.</param>
/// <param name="FailedPayloadCount">How many it left in the database because a copy could not be verified.</param>
/// <param name="MovedByteCount">How many bytes of raw MIME the copied payloads carried.</param>
/// <param name="EndedAt">When it reached the end of the content, absent while it has not.</param>
internal sealed record ContentMoveRun(
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("requestedAt")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("copiedPayloadCount")] long CopiedPayloadCount,
    [property: JsonPropertyName("failedPayloadCount")] long FailedPayloadCount,
    [property: JsonPropertyName("movedByteCount")] long MovedByteCount,
    [property: JsonPropertyName("endedAt")] DateTimeOffset? EndedAt)
{
    /// <summary>The deployment's word for a move its passes are carrying.</summary>
    internal const string RunningName = "running";

    /// <summary>The deployment's word for a move an operator stopped.</summary>
    internal const string PausedName = "paused";

    /// <summary>The deployment's word for a move whose walk reached the end of the content.</summary>
    internal const string CompletedName = "completed";

    /// <summary>Describes where the move stands in one line an operator reads.</summary>
    /// <returns>What it is doing, and when it finished where it has.</returns>
    internal string DescribeState() => this.State switch
    {
        RunningName => "under way",
        PausedName => "paused",
        CompletedName when this.EndedAt is { } endedAt =>
            $"finished at {endedAt.ToString("u", CultureInfo.InvariantCulture)}",
        CompletedName => "finished",
        _ => this.State ?? "in a state this version of the command does not know",
    };

    /// <summary>Describes what the move has carried so far, in counts and nothing derived from a message.</summary>
    /// <returns>The counts, grouped invariantly for the reason every other figure this tool prints is.</returns>
    internal string DescribeProgress() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.CopiedPayloadCount:N0} moved carrying {this.MovedByteCount:N0} bytes, {this.FailedPayloadCount:N0} left in the database");
}
