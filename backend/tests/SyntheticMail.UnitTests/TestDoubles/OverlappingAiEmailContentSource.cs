// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.SyntheticMail.Generation.AiContent;

namespace MailFathom.SyntheticMail.UnitTests.TestDoubles;

/// <summary>A source that answers asynchronously, counts how many answers were being waited for at once, and can hold them.</summary>
/// <param name="holdsAnswers">Whether every call waits for <see cref="Answer" /> before it is answered.</param>
/// <remarks>
/// <para>
/// The seam the overlapping generation is measured through. <see cref="ScriptedAiEmailContentSource" /> answers
/// synchronously, so a generation driven by it runs to completion inside the loop that started it and proves nothing
/// about concurrency either way; this one always yields first, which is what puts several calls in flight.
/// </para>
/// <para>
/// Nothing here waits on a clock. Holding an answer is a <see cref="TaskCompletionSource" /> the test completes, and
/// waiting for the calls to arrive is another one the source completes, so a test states an order rather than a
/// duration and a loaded machine cannot change the result.
/// </para>
/// </remarks>
internal sealed class OverlappingAiEmailContentSource(bool holdsAnswers = false) : IAiEmailContentSource
{
    private readonly TaskCompletionSource answering = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock guard = new();
    private readonly List<AiEmailContentRequest> requests = [];
    private readonly Dictionary<int, TaskCompletionSource> arrivals = [];
    private int inFlight;

    /// <summary>The most answers the generation waited for at once.</summary>
    internal int PeakInFlight { get; private set; }

    /// <summary>The questions asked, in the order they arrived.</summary>
    internal IReadOnlyList<AiEmailContentRequest> Requests
    {
        get
        {
            lock (this.guard)
            {
                return [.. this.requests];
            }
        }
    }

    /// <summary>Answers every held call, and every call that arrives after this.</summary>
    internal void Answer() => this.answering.TrySetResult();

    /// <summary>Completes once the named number of calls has arrived.</summary>
    /// <param name="calls">How many calls to wait for.</param>
    /// <returns>The wait.</returns>
    internal Task AskedAsync(int calls)
    {
        lock (this.guard)
        {
            if (this.requests.Count >= calls)
            {
                return Task.CompletedTask;
            }

            if (!this.arrivals.TryGetValue(calls, out var arrival))
            {
                arrival = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                this.arrivals[calls] = arrival;
            }

            return arrival.Task;
        }
    }

    /// <inheritdoc />
    public async Task<AiEmailContent> GenerateAsync(AiEmailContentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (this.guard)
        {
            this.requests.Add(request);
            this.inFlight++;
            this.PeakInFlight = Math.Max(this.PeakInFlight, this.inFlight);

            if (this.arrivals.TryGetValue(this.requests.Count, out var arrival))
            {
                arrival.SetResult();
            }
        }

        // Always asynchronous, so the caller cannot finish a call inside the loop that started it.
        await Task.Yield();

        if (holdsAnswers)
        {
            await this.answering.Task.WaitAsync(cancellationToken);
        }

        lock (this.guard)
        {
            this.inFlight--;
        }

        // The answer is a function of the question and of nothing else, which is what makes "the same seed produces
        // the same corpus" assertable here at all: a source answering from a call counter would answer one message
        // differently depending on which call finished first, and the corpus would differ for the source's reason
        // rather than the generator's.
        var subject = string.Create(
            CultureInfo.InvariantCulture,
            $"{request.AuthorName} on {request.Topic} in {request.LanguageCode} answering {request.ParentSubject ?? "nobody"}");

        return new AiEmailContent(
            subject,
            string.Create(CultureInfo.InvariantCulture, $"Hello.\n\n{subject}.\n\nRegards"),
            string.Create(CultureInfo.InvariantCulture, $"<html><body><p>{subject}</p></body></html>"));
    }
}
