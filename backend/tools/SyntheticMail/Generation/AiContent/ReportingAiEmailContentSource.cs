// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.SyntheticMail.Generation.AiContent;

/// <summary>Reports each answered message as it lands, and answers whatever the source it wraps answers.</summary>
/// <param name="source">The source the run actually generates through.</param>
/// <param name="console">Where the count is reported, which is standard error and never the corpus.</param>
/// <param name="total">How many messages the run is generating, which is what the count is out of.</param>
/// <remarks>
/// <para>
/// A decorator rather than a report the generator writes, because progress is about the run rather than about the
/// corpus: the generator reaches nothing and says nothing, and this is the one place that knows both that a call
/// finished and where a run reports. It counts answers rather than calls, so a message the provider refused is
/// absent from the count rather than counted as produced.
/// </para>
/// <para>
/// The count is the only thing reported. The prompt and the answer never reach a log for the reason the source
/// itself gives — both are message content — and a run watching two hundred generations wants to know how far it
/// has got rather than what was written.
/// </para>
/// </remarks>
internal sealed class ReportingAiEmailContentSource(
    IAiEmailContentSource source,
    ISyntheticMailConsole console,
    int total) : IAiEmailContentSource
{
    private int answered;

    /// <inheritdoc />
    public async Task<AiEmailContent> GenerateAsync(AiEmailContentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var content = await source.GenerateAsync(request, cancellationToken);

        // Several answers land at once, so the counter is incremented atomically and the line is written from
        // whichever call finished. What a reader gets is a rising count rather than an order.
        console.WriteError(string.Create(
            CultureInfo.InvariantCulture,
            $"Generated {Interlocked.Increment(ref this.answered)} of {total} messages."));

        return content;
    }
}
