// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Operations;

/// <summary>Reports how much stands at each stage of an outbox, which is the first question an operator asks of one.</summary>
/// <remarks>
/// <para>
/// Every declared stage is present whether or not anything stands at it, because a stage that vanished when it emptied
/// would make a healthy outbox and a build that no longer records that stage look identical. Zero is an answer.
/// </para>
/// <para>
/// Counts and stage names are the whole of it. Nothing here names a recipient, a subject, or a message, so the summary
/// is safe to print on a terminal, to screenshot, and to publish as the dimensions of a gauge.
/// </para>
/// </remarks>
public sealed record OutboxSummary
{
    private OutboxSummary(IReadOnlyList<OutboxStageCount> stages) => this.Stages = stages;

    /// <summary>Gets one count per declared stage, in the order the stages are declared.</summary>
    public IReadOnlyList<OutboxStageCount> Stages { get; }

    /// <summary>Gets how many sends are waiting for something to happen to them.</summary>
    /// <remarks>
    /// It is the depth of the outbox in the sense an operator means: the sends nothing has finished with. The terminal
    /// stages are history rather than backlog, and summing them into a single figure would make an instance that has
    /// sent a great deal look like one that is stuck.
    /// </remarks>
    public int OutstandingCount => this.Stages
        .Where(stage => stage.Stage is OutgoingEmailStage.Recorded or OutgoingEmailStage.TransmissionBegun)
        .Sum(stage => stage.Count);

    /// <summary>Composes the summary from what a store counted, filling in the stages it counted nothing at.</summary>
    /// <param name="counted">The counts the store read, at most one per stage.</param>
    /// <returns>The summary, with one entry per declared stage.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="counted" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when two entries name the same stage.</exception>
    public static OutboxSummary Of(IReadOnlyList<OutboxStageCount> counted)
    {
        ArgumentNullException.ThrowIfNull(counted);

        var countsByStage = counted.ToDictionary(count => count.Stage, count => count.Count);

        return new OutboxSummary(
        [
            .. Enum.GetValues<OutgoingEmailStage>()
                .Select(stage => new OutboxStageCount(
                    stage,
                    countsByStage.TryGetValue(stage, out var count) ? count : 0)),
        ]);
    }

    /// <summary>Reports how many sends stand at one stage.</summary>
    /// <param name="stage">The stage to read.</param>
    /// <returns>The count, which is zero for a stage nothing stands at.</returns>
    public int CountOf(OutgoingEmailStage stage) =>
        this.Stages.FirstOrDefault(counted => counted.Stage == stage)?.Count ?? 0;
}
