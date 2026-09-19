// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.BodyCleanup;
using MailFathom.AI.Orchestration;
using MailFathom.Application.EmailContent.Cleaning;
using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.BodyCleanup;

/// <summary>Puts one corpus body's outline to the body-cleanup agent and holds the partition to the blocks a reader keeps.</summary>
/// <remarks>
/// The answer is read through <see cref="MailBodyCleanupReading" /> and then checked against the outline by
/// <see cref="MailBodyCleaningSegments" />, the two steps a reading pane takes before it draws a cleaned body, so a
/// partition the pane would have refused is a shortfall here too. No case reads two ways, so none is judged.
/// </remarks>
internal static class MailBodyCleanupScenario
{
    /// <summary>The name every case's scenario name begins with.</summary>
    public const string Name = "MailBodyCleanup";

    /// <summary>The name the deterministic verdict is recorded under.</summary>
    public const string ExpectationMetricName = "Partition as expected";

    /// <summary>Describes one body as the case the shared scenario runs.</summary>
    /// <param name="scenario">The body.</param>
    /// <param name="cancellationToken">Withdraws the rendering the outline is described from.</param>
    /// <returns>The request.</returns>
    public static async Task<StructuredAnswerRequest> RequestForAsync(
        MailBodyCleanupCase scenario,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var outline = await scenario.DescribeAsync(cancellationToken);

        return new StructuredAnswerRequest(
            $"{Name}.{scenario.Name}",
            MailBodyCleanupInstructions.Text,
            MailBodyCleanupInstructions.ComposeOutlineTurn(outline),
            static (model, plan) => MailBodyCleanupAgentComposition.Compose(
                model,
                plan,
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance),
            answer => ShortfallOf(answer, outline),
            ExpectationMetricName,
            StructuredAnswerScenario.JudgedWhen(readsTwoWays: false));
    }

    private static string? ShortfallOf(string answer, CleanableMailBody outline)
    {
        var kept = MailBodyCleaningSegments.Read(MailBodyCleanupReading.Read(answer), outline.Blocks.Count);

        if (kept is null)
        {
            return "the ranges do not partition the outline, so the reader would be shown the uncleaned message.";
        }

        var dropped = outline.Blocks.Where(block => !kept.Contains(block.Index)).ToArray();

        return dropped.Length is 0
            ? null
            : $"dropped {dropped.Length} of {outline.Blocks.Count} blocks the sender wrote: {string.Join("; ", dropped.Select(static block => $"{block.Index} \"{block.Opening}\""))}";
    }
}
