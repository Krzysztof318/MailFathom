// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Spam;

/// <summary>One page of what a deployment's classification concluded about an account's mail.</summary>
/// <param name="Classifications">The classifications, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record SpamClassificationPage(
    [property: JsonPropertyName("classifications")] IReadOnlyList<SpamClassificationReading> Classifications,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>What classification concluded about one message.</summary>
/// <param name="Email">The stable local identity of the message, which every other read names it by.</param>
/// <param name="Folder">The deployment's own name for the folder the message is in.</param>
/// <param name="Verdict">What was concluded.</param>
/// <param name="DecidedBy">Which stage reached the verdict.</param>
/// <param name="Score">The score reached, absent when no stage produced a number.</param>
/// <param name="Threshold">The score it was judged against, absent exactly when <paramref name="Score" /> is.</param>
/// <param name="CorpusRevision">The scanner rule corpus the deciding stage ran under, absent when it has none.</param>
/// <param name="Profile">The settings the verdict was reached under, absent on a record written before it named one.</param>
/// <param name="Signals">The names of the facts the verdict rests on.</param>
/// <param name="EvaluatedAt">When the classification was evaluated.</param>
/// <param name="RequestedMutations">The changes the verdict asked the mailbox for, empty where it asked for none.</param>
internal sealed record SpamClassificationReading(
    [property: JsonPropertyName("email")] Guid Email,
    [property: JsonPropertyName("folder")] string? Folder,
    [property: JsonPropertyName("verdict")] string? Verdict,
    [property: JsonPropertyName("decidedBy")] string? DecidedBy,
    [property: JsonPropertyName("score")] double? Score,
    [property: JsonPropertyName("threshold")] double? Threshold,
    [property: JsonPropertyName("corpusRevision")] string? CorpusRevision,
    [property: JsonPropertyName("profile")] string? Profile,
    [property: JsonPropertyName("signals")] IReadOnlyList<string> Signals,
    [property: JsonPropertyName("evaluatedAt")] DateTimeOffset EvaluatedAt,
    [property: JsonPropertyName("requestedMutations")] IReadOnlyList<SpamRequestedMutation> RequestedMutations)
{
    /// <summary>Describes the verdict and the number behind it in one line.</summary>
    /// <returns>The verdict, the deciding stage, and the score against its threshold where there is one.</returns>
    internal string DescribeVerdict() => this.Score is { } score && this.Threshold is { } threshold
        ? string.Create(CultureInfo.InvariantCulture, $"{this.Verdict} ({this.DecidedBy} {score:0.##}/{threshold:0.##})")
        : $"{this.Verdict} ({this.DecidedBy})";

    /// <summary>Describes what the verdict asked the mailbox for, naming each change and the record carrying it.</summary>
    /// <returns>The changes, or that it asked for none.</returns>
    internal string DescribeRequestedMutations() => this.RequestedMutations.Count == 0
        ? "none"
        : string.Join(", ", this.RequestedMutations.Select(static requested => requested.Describe()));
}

/// <summary>One change a verdict asked the mailbox for.</summary>
/// <param name="Record">The durable mutation record the account's convergence pass carries.</param>
/// <param name="Mutation">What was asked for.</param>
internal sealed record SpamRequestedMutation(
    [property: JsonPropertyName("record")] Guid Record,
    [property: JsonPropertyName("mutation")] string? Mutation)
{
    /// <summary>Describes the change and the record that carries it.</summary>
    /// <returns>The mutation's name and the identifier the mutation audit trail answers to.</returns>
    internal string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.Mutation} ({this.Record:D})");
}
