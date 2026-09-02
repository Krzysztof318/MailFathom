// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Reads the embedding family's two dimensions off a recorded measurement as one comparable value.</summary>
/// <remarks>
/// Beside the shared recorder rather than inside it: every boundary records the same way, and only this family reads
/// two of its dimensions together. The two are rendered together because they are read together — a series split on
/// the outcome alone would not distinguish two provider failures, and one split on the failure alone would not
/// distinguish a success from an instance that embeds nothing.
/// </remarks>
internal static class EmbeddingMeasurementTags
{
    private const string OutcomeTagName = "mailfathom.embedding.outcome";
    private const string FailureTagName = "mailfathom.embedding.failure";

    /// <summary>Gets the tag pairs one instrument published, in order, rendered as <c>outcome/failure</c>.</summary>
    /// <param name="measurements">What the meter recorded.</param>
    /// <param name="instrumentName">The instrument to read.</param>
    /// <returns>One rendered pair per measurement.</returns>
    internal static IReadOnlyList<string> TagsOf(
        this RecordedMailFathomMeasurements measurements,
        string instrumentName) =>
        [.. measurements.Read(instrumentName).Select(measurement =>
            $"{measurement.Tags.GetValueOrDefault(OutcomeTagName)}/{measurement.Tags.GetValueOrDefault(FailureTagName)}")];
}
