// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>A job handler that reports the most jobs that were ever inside it at one moment.</summary>
/// <remarks>
/// <para>
/// It holds every job it is given until as many are inside it as the expected ceiling, and only then lets them all go.
/// That is what makes the measurement decide the test rather than the scheduler: an unbounded dispatcher starts every
/// job before any of them can suspend, so the peak it observes is the whole batch, while a bounded one physically
/// cannot admit more than the ceiling and the peak is the ceiling exactly.
/// </para>
/// <para>
/// The expected ceiling must therefore be reachable — never more jobs than the batch holds — because a ceiling nothing
/// reaches leaves every job waiting inside the handler for a release that never comes.
/// </para>
/// </remarks>
internal sealed class ConcurrencyObservingJobHandler : IJobHandler
{
    private readonly Lock observation = new();
    private readonly TaskCompletionSource ceilingReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int expectedCeiling;
    private int running;

    internal ConcurrencyObservingJobHandler(JobType jobType, int expectedCeiling)
    {
        this.JobType = jobType;
        this.expectedCeiling = expectedCeiling;
    }

    /// <inheritdoc />
    public JobType JobType { get; }

    /// <summary>Gets the most jobs that were inside the handler at any one moment.</summary>
    internal int PeakConcurrency { get; private set; }

    /// <summary>Gets how many times the handler was run.</summary>
    internal int RunCount { get; private set; }

    /// <inheritdoc />
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        lock (this.observation)
        {
            this.running++;
            this.RunCount++;
            this.PeakConcurrency = Math.Max(this.PeakConcurrency, this.running);

            if (this.running >= this.expectedCeiling)
            {
                this.ceilingReached.TrySetResult();
            }
        }

        await this.ceilingReached.Task;

        lock (this.observation)
        {
            this.running--;
        }
    }
}
